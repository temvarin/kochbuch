// Kochbuch on Azure (Consumption / Flex Consumption, scale-to-zero, ~$0.02-0.05/month excl. shared SQL).
// Deploy: az deployment group create -g rg-kochbuch -f infra/main.bicep -p infra/main.bicepparam

targetScope = 'resourceGroup'

@description('Region of the resource group (must be allowed on the Azure for Students subscription).')
param location string = resourceGroup().location

@description('Prefix for all resource names.')
param namePrefix string = 'kochbuch'

@description('Registry and owner for public GHCR images.')
param registry string

@description('Container image tag (commit SHA).')
param imageTag string

@description('Comma-separated Entra object IDs of the household members.')
param members string

@description('Microsoft Entra tenant ID for Easy Auth.')
param tenantId string = tenant().tenantId

@description('Client ID of the Entra app registration for sign-in.')
param signInClientId string

@description('Name of the existing Azure SQL logical server (without domain).')
param sqlServerName string

@description('Name of the existing Azure SQL database hosting the recipes and shopping schemas.')
param sqlDatabaseName string

@description('Monthly budget in the billing currency; mails at 50% and 100%.')
param budgetAmount int = 3

@description('Budget start date (yyyy-MM-01, not in the past at creation).')
param budgetStartDate string

@description('Email addresses for the budget alarm.')
param budgetContactEmails array

var suffix = take(uniqueString(resourceGroup().id), 6)
var storageName = take('st${replace(namePrefix, '-', '')}${uniqueString(resourceGroup().id)}', 24)
var blobEndpoint = 'https://${storageName}.blob.${az.environment().suffixes.storage}/'
var sqlServer = 'tcp:${sqlServerName}${az.environment().suffixes.sqlServerHostname},1433'

var roles = {
  blobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  blobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  blobDataReader: '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
  blobDelegator: 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'
  serviceBusSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  serviceBusReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
}

// -----------------------------------------------------------------------------
// 1. Observability (free 5 GB/month tier, 0.1 GB/day cap) & Budget Guardrail
// -----------------------------------------------------------------------------

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${namePrefix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.1') }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${namePrefix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'budget-${namePrefix}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      half: { enabled: true, operator: 'GreaterThan', threshold: 50, thresholdType: 'Actual', contactEmails: budgetContactEmails }
      all: { enabled: true, operator: 'GreaterThan', threshold: 100, thresholdType: 'Actual', contactEmails: budgetContactEmails }
    }
  }
}

// -----------------------------------------------------------------------------
// 2. Managed Identities (zero secrets anywhere)
// -----------------------------------------------------------------------------

resource gatewayIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-gateway'
  location: location
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-api'
  location: location
}

resource mediaIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-media'
  location: location
}

// -----------------------------------------------------------------------------
// 3. Container Apps Environment + Gateway (External) + API (Internal)
// -----------------------------------------------------------------------------

resource managedEnv 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: 'cae-${namePrefix}'
  location: location
  properties: {
    environmentMode: 'WorkloadProfiles'
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    peerTrafficConfiguration: { encryption: { enabled: true } }
  }
}

resource apiApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    environmentId: managedEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
        allowInsecure: true
      }
    }
    template: {
      containers: [
        {
          name: '${namePrefix}-api'
          image: '${registry}/${namePrefix}-api:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            {
              name: 'ConnectionStrings__Database'
              value: 'Server=${sqlServer};Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${apiIdentity.properties.clientId};Encrypt=True'
            }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 30 }
            { type: 'Startup', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 2, failureThreshold: 30 }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '20' } } }]
      }
    }
  }
}

resource gatewayApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: '${namePrefix}-gateway'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${gatewayIdentity.id}': {} }
  }
  properties: {
    environmentId: managedEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      secrets: [
        // Tells Easy Auth to prove itself to Entra using the gateway's federated managed identity (no client secret).
        { name: 'override-use-mi-fic-assertion-client-id', value: gatewayIdentity.properties.clientId }
      ]
    }
    template: {
      containers: [
        {
          name: '${namePrefix}-gateway'
          image: '${registry}/${namePrefix}-gateway:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'Koch__EasyAuth', value: 'true' }
            { name: 'Koch__Members', value: members }
            { name: 'Services__Api', value: 'http://${apiApp.name}' }
            { name: 'Photos__BlobServiceUri', value: blobEndpoint }
            { name: 'AZURE_CLIENT_ID', value: gatewayIdentity.properties.clientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 30 }
            { type: 'Startup', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 2, failureThreshold: 30 }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '20' } } }]
      }
    }
  }
}

resource gatewayAuth 'Microsoft.App/containerApps/authConfigs@2025-01-01' = {
  parent: gatewayApp
  name: 'current'
  properties: {
    platform: { enabled: true }
    globalValidation: { unauthenticatedClientAction: 'AllowAnonymous' }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: signInClientId
          clientSecretSettingName: 'override-use-mi-fic-assertion-client-id'
          openIdIssuer: '${environment().authentication.loginEndpoint}${tenantId}/v2.0'
        }
      }
    }
    login: {
      cookieExpiration: { convention: 'FixedTime', timeToExpiration: '30.00:00:00' }
      preserveUrlFragmentsForLogins: true
    }
  }
}

// -----------------------------------------------------------------------------
// 4. Photo Storage (uploads/, photos/, deployments/, deadletter/)
// -----------------------------------------------------------------------------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    cors: {
      corsRules: [
        {
          allowedOrigins: ['https://${gatewayApp.name}.${managedEnv.properties.defaultDomain}']
          allowedMethods: ['PUT', 'OPTIONS']
          allowedHeaders: ['content-type', 'x-ms-blob-type', 'x-ms-version', 'x-ms-client-request-id']
          exposedHeaders: ['x-ms-request-id']
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

var containerNames = ['uploads', 'photos', 'deployments', 'deadletter']

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for name in containerNames: {
  parent: blobService
  name: name
  properties: { publicAccess: 'None' }
}]

resource blobLifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'stale-uploads'
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['uploads/'] }
            actions: { baseBlob: { delete: { daysAfterCreationGreaterThan: 1 } } }
          }
        }
        {
          name: 'old-dead-letters'
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['deadletter/'] }
            actions: { baseBlob: { delete: { daysAfterCreationGreaterThan: 30 } } }
          }
        }
      ]
    }
  }
}

resource gatewayDelegator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, gatewayIdentity.id, roles.blobDelegator)
  properties: { principalId: gatewayIdentity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDelegator) }
}

resource gatewayUploads 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containers[0]
  name: guid(containers[0].id, gatewayIdentity.id, roles.blobDataContributor)
  properties: { principalId: gatewayIdentity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor) }
}

resource gatewayPhotos 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containers[1]
  name: guid(containers[1].id, gatewayIdentity.id, roles.blobDataReader)
  properties: { principalId: gatewayIdentity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataReader) }
}

resource mediaBlobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, mediaIdentity.id, roles.blobDataOwner)
  properties: { principalId: mediaIdentity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataOwner) }
}

// -----------------------------------------------------------------------------
// 5. Async Photo Pipeline: Event Grid -> Service Bus Basic -> Media Function
// -----------------------------------------------------------------------------

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${namePrefix}-${suffix}'
  location: location
  sku: { name: 'Basic', tier: 'Basic' }
  properties: { disableLocalAuth: true, minimumTlsVersion: '1.2' }
}

resource uploadsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'photo-uploads'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT2M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
  }
}

resource uploadTopic 'Microsoft.EventGrid/systemTopics@2024-06-01-preview' = {
  name: 'evgt-${namePrefix}-photos'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: { source: storage.id, topicType: 'Microsoft.Storage.StorageAccounts' }
}

resource topicSends 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: uploadsQueue
  name: guid(uploadsQueue.id, uploadTopic.id, roles.serviceBusSender)
  properties: { principalId: uploadTopic.identity.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusSender) }
}

resource topicDeadLetters 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, uploadTopic.id, roles.blobDataContributor)
  properties: { principalId: uploadTopic.identity.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor) }
}

resource mediaReceives 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: uploadsQueue
  name: guid(uploadsQueue.id, mediaIdentity.id, roles.serviceBusReceiver)
  properties: { principalId: mediaIdentity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusReceiver) }
}

resource uploadSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2024-06-01-preview' = {
  parent: uploadTopic
  name: 'uploads-to-media'
  dependsOn: [topicSends, topicDeadLetters]
  properties: {
    eventDeliverySchema: 'EventGridSchema'
    filter: {
      includedEventTypes: ['Microsoft.Storage.BlobCreated']
      subjectBeginsWith: '/blobServices/default/containers/uploads/blobs/'
    }
    deliveryWithResourceIdentity: {
      identity: { type: 'SystemAssigned' }
      destination: { endpointType: 'ServiceBusQueue', properties: { resourceId: uploadsQueue.id } }
    }
    retryPolicy: { maxDeliveryAttempts: 10, eventTimeToLiveInMinutes: 1440 }
    deadLetterWithResourceIdentity: {
      identity: { type: 'SystemAssigned' }
      deadLetterDestination: { endpointType: 'StorageBlob', properties: { resourceId: storage.id, blobContainerName: 'deadletter' } }
    }
  }
}

resource mediaPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${namePrefix}-media'
  location: location
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

resource mediaApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${namePrefix}-media-${suffix}'
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${mediaIdentity.id}': {} }
  }
  properties: {
    serverFarmId: mediaPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}deployments'
          authentication: { type: 'UserAssignedIdentity', userAssignedIdentityResourceId: mediaIdentity.id }
        }
      }
      scaleAndConcurrency: { instanceMemoryMB: 2048, maximumInstanceCount: 10 }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: mediaIdentity.properties.clientId }
        { name: 'ServiceBus__fullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
        { name: 'ServiceBus__credential', value: 'managedidentity' }
        { name: 'ServiceBus__clientId', value: mediaIdentity.properties.clientId }
        { name: 'Photos__BlobServiceUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'AZURE_CLIENT_ID', value: mediaIdentity.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
      ]
    }
  }
}

resource noFtpPassword 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: mediaApp
  name: 'ftp'
  properties: { allow: false }
}

resource noScmPassword 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: mediaApp
  name: 'scm'
  properties: { allow: false }
}

output gatewayFqdn string = gatewayApp.properties.configuration.ingress.fqdn
output mediaFunctionName string = mediaApp.name
output gatewayIdentityClientId string = gatewayIdentity.properties.clientId

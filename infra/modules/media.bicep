// Koch.Media as an Azure Function on Flex Consumption: no instance runs while no photo waits, and the monthly free grant
// covers a household's photos. Everything it reaches, it reaches with its own managed identity (docs/adr/0006).

param location string
param namePrefix string
param identityId string
param identityClientId string
param storageAccountName string
param blobEndpoint string
param serviceBusNamespace string
param insightsConnectionString string

var suffix = take(uniqueString(resourceGroup().id), 6)

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${namePrefix}-media'
  location: location
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${namePrefix}-media-${suffix}'
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${blobEndpoint}deployments'
          authentication: { type: 'UserAssignedIdentity', userAssignedIdentityResourceId: identityId }
        }
      }
      scaleAndConcurrency: {
        // Decoding a 12-megapixel photo takes a few hundred MB; 2 GB leaves room for two at once (host.json).
        instanceMemoryMB: 2048
        maximumInstanceCount: 10
      }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        // The host's own state, by identity: no storage key.
        { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identityClientId }
        // The queue trigger's connection "ServiceBus", by identity.
        { name: 'ServiceBus__fullyQualifiedNamespace', value: serviceBusNamespace }
        { name: 'ServiceBus__credential', value: 'managedidentity' }
        { name: 'ServiceBus__clientId', value: identityClientId }
        { name: 'Photos__BlobServiceUri', value: blobEndpoint }
        { name: 'AZURE_CLIENT_ID', value: identityClientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insightsConnectionString }
      ]
    }
  }
}

// CI deploys with its Entra identity (OIDC), so the publishing user name and password the platform would otherwise
// create for FTP and the deployment endpoint (scm) are switched off: no password exists that could deploy code.
resource noFtpPassword 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: { allow: false }
}

resource noScmPassword 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: { allow: false }
}

output name string = app.name

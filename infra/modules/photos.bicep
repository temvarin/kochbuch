// Media's storage (Koch.Media.Contracts.PhotoPaths): uploads/ that phones write to with a short-lived link, photos/
// with the renditions, plus the Function's own deployment package and Event Grid's dead letters. No account keys exist:
// every access is by managed identity, and the upload links are user delegation SAS (docs/adr/0006).

param location string
param storageName string

@description('The app\'s origin; the only one allowed to PUT uploads from a browser (CORS).')
param appOrigin string

@description('Principal ids of the managed identities that get data roles.')
param gatewayPrincipalId string
param mediaPrincipalId string

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
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

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    cors: {
      corsRules: [
        {
          allowedOrigins: [appOrigin]
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
  parent: blobs
  name: name
  properties: { publicAccess: 'None' }
}]

// Uploads are processed within minutes; one still there after a day was abandoned (the phone went offline mid-way) or
// refused. Event Grid's dead letters are kept a month for a look.
// Renditions are not deleted here but by Media's daily sweep: "detached for 30 days" needs a comparison on the
// detach date tag, and lifecycle filters only test tags for equality (docs/adr/0008).
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: account
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

// Built-in role ids (https://learn.microsoft.com/azure/role-based-access-control/built-in-roles).
var roles = {
  blobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  blobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  blobDataReader: '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
  blobDelegator: 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'
}

// The gateway: sign upload links (delegator, account-wide by nature), and only what the links may do: write in uploads.
// Reading photos is for serving renditions. It can't touch anything else.
resource gatewayDelegator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, gatewayPrincipalId, roles.blobDelegator)
  properties: { principalId: gatewayPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDelegator) }
}

resource gatewayUploads 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containers[0]
  name: guid(containers[0].id, gatewayPrincipalId, roles.blobDataContributor)
  properties: { principalId: gatewayPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor) }
}

resource gatewayPhotos 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: containers[1]
  name: guid(containers[1].id, gatewayPrincipalId, roles.blobDataReader)
  properties: { principalId: gatewayPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataReader) }
}

// Media: the Functions host keeps its own state in this account (AzureWebJobsStorage) and deploys from it, and Media
// reads uploads and writes renditions: owner of the account's blobs.
resource mediaOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, mediaPrincipalId, roles.blobDataOwner)
  properties: { principalId: mediaPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataOwner) }
}

output id string = account.id
output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob

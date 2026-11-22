// From "a photo was uploaded" to Media: Blob Storage tells Event Grid (system topic), Event Grid filters for BlobCreated in
// uploads/ and delivers into a Service Bus queue, and Media's Function takes it from there. The queue buffers while Media
// is cold or failing and dead-letters what keeps failing; Event Grid itself dead-letters to a blob what it can't deliver
// (DDIA ch12 p.491-493; docs/adr/0006, 0007).

param location string
param namePrefix string
param storageAccountName string

@description('Media\'s managed identity: receives from both queues.')
param mediaPrincipalId string

@description('Recipes\' managed identity: sends its outbox\'s photo events (docs/adr/0008).')
param recipesPrincipalId string

var suffix = take(uniqueString(resourceGroup().id), 6)

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

// Basic: queues only, a few cents per million operations. Topics need Standard (~$10 a month) and there is one consumer
// (docs/adr/0007). Local (SAS key) auth is off: only Entra identities.
resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${namePrefix}-${suffix}'
  location: location
  sku: { name: 'Basic', tier: 'Basic' }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource uploads 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespace
  name: 'photo-uploads'
  properties: {
    // A photo that fails five times (e.g. storage away for longer) goes to the dead-letter queue; an alert watches it.
    maxDeliveryCount: 5
    lockDuration: 'PT2M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
  }
}

// Recipes' outbox relay → Media: a photo was attached to or detached from a recipe. Each event carries a sequence, so
// Media can take them in any order and more than once (docs/adr/0008). Kept a while: the events change nothing urgent,
// and one that waits for a failing Media mustn't expire before the dead-letter alert is seen.
resource lifecycle 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespace
  name: 'photo-lifecycle'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
  }
}

resource topic 'Microsoft.EventGrid/systemTopics@2024-06-01-preview' = {
  name: 'evgt-${namePrefix}-photos'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    source: storage.id
    topicType: 'Microsoft.Storage.StorageAccounts'
  }
}

var roles = {
  serviceBusSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  serviceBusReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  blobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

resource topicSends 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: uploads
  name: guid(uploads.id, topic.id, roles.serviceBusSender)
  properties: { principalId: topic.identity.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusSender) }
}

resource deadLetters 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' existing = {
  name: '${storageAccountName}/default/deadletter'
}

resource topicDeadLetters 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: deadLetters
  name: guid(deadLetters.id, topic.id, roles.blobDataContributor)
  properties: { principalId: topic.identity.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor) }
}

resource mediaReceives 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: uploads
  name: guid(uploads.id, mediaPrincipalId, roles.serviceBusReceiver)
  properties: { principalId: mediaPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusReceiver) }
}

resource recipesSends 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: lifecycle
  name: guid(lifecycle.id, recipesPrincipalId, roles.serviceBusSender)
  properties: { principalId: recipesPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusSender) }
}

resource mediaReceivesLifecycle 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: lifecycle
  name: guid(lifecycle.id, mediaPrincipalId, roles.serviceBusReceiver)
  properties: { principalId: mediaPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusReceiver) }
}

resource subscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2024-06-01-preview' = {
  parent: topic
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
      destination: {
        endpointType: 'ServiceBusQueue'
        properties: { resourceId: uploads.id }
      }
    }
    retryPolicy: {
      maxDeliveryAttempts: 10
      eventTimeToLiveInMinutes: 1440
    }
    deadLetterWithResourceIdentity: {
      identity: { type: 'SystemAssigned' }
      deadLetterDestination: {
        endpointType: 'StorageBlob'
        properties: { resourceId: storage.id, blobContainerName: 'deadletter' }
      }
    }
  }
}

output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
output namespaceId string = namespace.id
output uploadTopicId string = topic.id
output queueNames array = [uploads.name, lifecycle.name]

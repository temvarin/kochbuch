// One user-assigned managed identity per deployable, so each can be granted exactly what it needs and nothing else
// (docs/adr/0003). User-assigned rather than system-assigned: the identity exists before the app, so the SQL grants
// (db/manual/grants.sql) and federated credentials can name it before the first revision runs.

param location string
param namePrefix string

@description('The deployables that get an identity.')
param names array

resource identities 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = [for name in names: {
  name: 'id-${namePrefix}-${name}'
  location: location
}]

output identities array = [for (name, i) in names: {
  name: name
  id: identities[i].id
  clientId: identities[i].properties.clientId
  principalId: identities[i].properties.principalId
  principalName: identities[i].name
}]

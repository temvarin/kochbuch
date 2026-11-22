// The Container Apps environment: the network boundary the services live in. Consumption only (no workload profiles,
// no VNet), which has no base fee: apps cost nothing while scaled to zero, and the monthly free grant covers the rest.
// Apps with internal ingress are reachable only from inside this environment (docs/adr/0002).

param location string
param namePrefix string
param workspaceName string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-${namePrefix}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    // Traffic between apps inside the environment is encrypted too, not only what comes in from outside.
    peerTrafficConfiguration: {
      encryption: { enabled: true }
    }
  }
}

output id string = environment.id
output defaultDomain string = environment.properties.defaultDomain

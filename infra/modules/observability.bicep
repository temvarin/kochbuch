// Where all apps send logs and traces: one Log Analytics workspace with a daily cap, and Application Insights on top
// of it. The cap keeps a runaway log loop from eating the student credit; the first 5 GB a month are free.

param location string
param namePrefix string

@description('Daily ingestion cap in GB. 0.1 GB/day stays inside the free 5 GB/month.')
param dailyCapGb string = '0.1'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${namePrefix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json(dailyCapGb) }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${namePrefix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    // Sampling and the workspace cap bound the cost; no local-auth keys, apps send with the connection string.
    IngestionMode: 'LogAnalytics'
  }
}

// The queries of infra/queries, saved in the workspace: Logs → Queries → "kochbuch" in the portal. The files are the
// source; tests/Koch.Architecture.Tests/QueryTests checks them against the tables (docs/adr/0009).
var queries = [
  { name: 'trace', title: 'One trace end to end', query: loadTextContent('../queries/trace.kql') }
  { name: 'failures', title: 'Failures of the last day', query: loadTextContent('../queries/failures.kql') }
  { name: 'latency', title: 'Latency percentiles and errors', query: loadTextContent('../queries/latency.kql') }
  { name: 'photo-events', title: 'Photo events applied by Media', query: loadTextContent('../queries/photo-events.kql') }
  { name: 'outbox-relay', title: 'Outbox relay rounds', query: loadTextContent('../queries/outbox-relay.kql') }
  { name: 'ingestion', title: 'Ingestion against the daily cap', query: loadTextContent('../queries/ingestion.kql') }
]

resource savedQueries 'Microsoft.OperationalInsights/workspaces/savedSearches@2020-08-01' = [for query in queries: {
  parent: workspace
  name: 'kochbuch-${query.name}'
  properties: {
    category: 'kochbuch'
    displayName: 'kochbuch: ${query.title}'
    query: query.query
  }
}]

output workspaceName string = workspace.name
output insightsId string = insights.id
output insightsConnectionString string = insights.properties.ConnectionString

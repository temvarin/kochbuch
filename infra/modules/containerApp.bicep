// One HTTP service as a Container App: scales to zero when idle (KEDA HTTP rule), one small replica at most, health
// probes on /healthz, its own managed identity. Used for the gateway and every service behind it.

param location string
param name string
param environmentId string
param image string
param identityId string

@description('true: reachable from the internet (the gateway). false: only from inside the environment.')
param external bool

@description('Plain settings as { name: value }. Secrets go through `secrets`.')
param settings object = {}

@description('Container App secrets as [{ name, value }]; settings refer to them with secretRef.')
@secure()
param secrets object = {}

@description('Settings whose value is a Container App secret, as { settingName: nameOfTheSecret }.')
param settingRefs object = {}

// A staged release (the gateway, docs/adr/0009): this deployment's revision is named <name>--<suffix> and starts with no
// traffic under the label "next", while stableRevision keeps all of it under "stable". CI checks the next revision at
// its own address, then moves the traffic. Without a suffix: one revision at a time, replaced on every deployment.
@description('Suffix of the revision this deployment makes, e.g. r57-1; empty for one revision at a time.')
param revisionSuffix string = ''

@description('The revision that has all the traffic now; empty on the first deployment (the new one then gets it).')
param stableRevision string = ''

var plainEnv = [for setting in items(settings): { name: setting.key, value: string(setting.value) }]
var refEnv = [for setting in items(settingRefs): { name: setting.key, secretRef: setting.value }]

var staged = !empty(revisionSuffix)
var newRevision = '${name}--${revisionSuffix}'
var hasStable = !empty(stableRevision) && stableRevision != newRevision
var stagedTraffic = hasStable
  ? [
      { revisionName: stableRevision, weight: 100, label: 'stable' }
      { revisionName: newRevision, weight: 0, label: 'next' }
    ]
  : [{ revisionName: newRevision, weight: 100, label: 'stable' }]

resource app 'Microsoft.App/containerApps@2025-01-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      // Staged: several revisions, traffic by label. Otherwise one at a time.
      activeRevisionsMode: staged ? 'Multiple' : 'Single'
      ingress: {
        external: external
        targetPort: 8080
        transport: 'http'
        // The internet only gets HTTPS. Inside the environment the gateway calls http://<app name>; refusing that would
        // answer with a redirect, which the proxy would pass on to the phone. That traffic is still encrypted by the
        // environment's peer encryption (environment.bicep).
        allowInsecure: !external
        traffic: staged ? stagedTraffic : null
      }
      secrets: secrets.?items ?? []
    }
    template: {
      revisionSuffix: staged ? revisionSuffix : null
      containers: [
        {
          name: name
          image: image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(plainEnv, refEnv)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Startup'
              httpGet: { path: '/healthz', port: 8080 }
              periodSeconds: 2
              failureThreshold: 30
            }
          ]
        }
      ]
      scale: {
        // Zero when idle: the free grant can't carry a replica running all month (docs/adr/0011).
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '20' } }
          }
        ]
      }
    }
  }
}

output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn

@description('The revision this deployment made, when staged.')
output revisionName string = staged ? newRevision : ''

@description('Whether the new revision waits at 0 % for CI to check it and move the traffic.')
output waitsForSwitch bool = staged && hasStable

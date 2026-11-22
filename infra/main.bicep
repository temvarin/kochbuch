// kochbuch in Azure: the gateway and the services behind it, in resource group rg-kochbuch. The database is
// kassensturz's existing Azure SQL database, which this template does not manage (docs/adr/0003, docs/deployment.md).
//
// Deploy: az deployment group create -g rg-kochbuch -f infra/main.bicep -p infra/main.bicepparam -p imageTag=<tag>

targetScope = 'resourceGroup'

@description('Region of the resource group. Must be in the subscription\'s allowed-regions list (Azure for Students).')
param location string = resourceGroup().location

@description('Prefix of every resource name.')
param namePrefix string = 'kochbuch'

@description('Registry and owner the images are pulled from, e.g. ghcr.io/temvarin. Public packages: no pull secret.')
param registry string

@description('Image tag to run, normally the commit SHA (CI passes it).')
param imageTag string

@description('Entra object ids of the household members, comma-separated: the same two ids as kassensturz.')
param members string

@description('Tenant of the app registration used for sign-in.')
param tenantId string = tenant().tenantId

@description('Client id of the Entra app registration used for sign-in (docs/deployment.md, step 3).')
param signInClientId string

@description('How Easy Auth proves itself to Entra (docs/adr/0010): a federated managed identity, or a client secret.')
@allowed(['managedIdentity', 'clientSecret'])
param authCredential string = 'managedIdentity'

@description('Client secret of the sign-in app registration; only used when authCredential is clientSecret.')
@secure()
param signInClientSecret string = ''

@description('Name of kassensturz\'s Azure SQL logical server (without the domain).')
param sqlServerName string

@description('Name of kassensturz\'s database, which gets the kochbuch schemas.')
param sqlDatabaseName string

@description('Suffix of the gateway revision this deployment makes, e.g. r57-1 (CI: run number and attempt). Empty: the gateway is replaced in place.')
param gatewayRevisionSuffix string = ''

@description('The gateway revision that has all traffic now; it keeps it until CI has checked the new one (docs/adr/0009).')
param gatewayStableRevision string = ''

@description('Monthly budget in the billing currency; an alarm mail goes out at 50 % and 100 %.')
param budgetAmount int = 3

@description('First day of the budget, yyyy-MM-01. Must not be in the past at creation.')
param budgetStartDate string

@description('Who gets the budget alarm and the alerts (infra/modules/alerts.bicep).')
param budgetContactEmails array

var deployables = ['gateway', 'recipes', 'shopping', 'media']
var appNames = {
  gateway: '${namePrefix}-gateway'
  recipes: '${namePrefix}-recipes'
  shopping: '${namePrefix}-shopping'
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: { location: location, namePrefix: namePrefix }
}

module identities 'modules/identities.bicep' = {
  name: 'identities'
  params: { location: location, namePrefix: namePrefix, names: deployables }
}

module environment 'modules/environment.bicep' = {
  name: 'environment'
  params: { location: location, namePrefix: namePrefix, workspaceName: observability.outputs.workspaceName }
}

var gatewayIdentity = identities.outputs.identities[0]
var recipesIdentity = identities.outputs.identities[1]
var shoppingIdentity = identities.outputs.identities[2]
var mediaIdentity = identities.outputs.identities[3]

// Photos (docs/adr/0006). The storage name is fixed up front, so the gateway can be told where uploads go before the
// account exists, and the account can be told the gateway's origin for CORS.
var storageName = take('st${replace(namePrefix, '-', '')}${uniqueString(resourceGroup().id)}', 24)
var blobEndpoint = 'https://${storageName}.blob.${az.environment().suffixes.storage}/'

// Every service reaches kassensturz's database with its own identity, which db/manual/grants.sql admits to its schema.
var sqlServer = 'tcp:${sqlServerName}${az.environment().suffixes.sqlServerHostname},1433'

// Easy Auth reads the credential from a Container App secret. With a managed identity, the "secret" is only the
// identity's client id, under the setting name Easy Auth recognises (docs/adr/0010).
var authSecretName = authCredential == 'managedIdentity'
  ? 'override-use-mi-fic-assertion-client-id'
  : 'microsoft-provider-authentication-secret'

module gateway 'modules/containerApp.bicep' = {
  name: 'gateway'
  params: {
    location: location
    name: appNames.gateway
    environmentId: environment.outputs.id
    image: '${registry}/${namePrefix}-gateway:${imageTag}'
    identityId: gatewayIdentity.id
    external: true
    revisionSuffix: gatewayRevisionSuffix
    stableRevision: gatewayStableRevision
    settings: {
      Koch__EasyAuth: 'true'
      Koch__Members: members
      // Internal ingress: inside the environment an app is reachable as http://<app name>.
      Services__recipes: 'http://${appNames.recipes}'
      Services__shopping: 'http://${appNames.shopping}'
      // Upload links and renditions, with the gateway's own identity (infra/modules/photos.bicep).
      Photos__BlobServiceUri: blobEndpoint
      AZURE_CLIENT_ID: gatewayIdentity.clientId
      APPLICATIONINSIGHTS_CONNECTION_STRING: observability.outputs.insightsConnectionString
    }
    secrets: {
      items: [
        {
          name: authSecretName
          value: authCredential == 'managedIdentity' ? gatewayIdentity.clientId : signInClientSecret
        }
      ]
    }
  }
}

module gatewayAuth 'modules/gatewayAuth.bicep' = {
  name: 'gatewayAuth'
  params: {
    appName: gateway.outputs.name
    tenantId: tenantId
    clientId: signInClientId
    secretName: authSecretName
  }
}

module recipes 'modules/containerApp.bicep' = {
  name: 'recipes'
  params: {
    location: location
    name: appNames.recipes
    environmentId: environment.outputs.id
    image: '${registry}/${namePrefix}-recipes:${imageTag}'
    identityId: recipesIdentity.id
    external: false
    settings: {
      // No password: the service logs in with its own managed identity, which db/manual/grants.sql admits to the
      // recipes schema only.
      ConnectionStrings__Recipes: 'Server=${sqlServer};Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${recipesIdentity.clientId};Encrypt=True'
      // The outbox relay sends photo events to the queue photo-lifecycle, with the same identity (docs/adr/0008).
      ServiceBus__FullyQualifiedNamespace: messaging.outputs.fullyQualifiedNamespace
      AZURE_CLIENT_ID: recipesIdentity.clientId
      APPLICATIONINSIGHTS_CONNECTION_STRING: observability.outputs.insightsConnectionString
    }
  }
}

module shopping 'modules/containerApp.bicep' = {
  name: 'shopping'
  params: {
    location: location
    name: appNames.shopping
    environmentId: environment.outputs.id
    image: '${registry}/${namePrefix}-shopping:${imageTag}'
    identityId: shoppingIdentity.id
    external: false
    settings: {
      ConnectionStrings__Shopping: 'Server=${sqlServer};Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${shoppingIdentity.clientId};Encrypt=True'
      APPLICATIONINSIGHTS_CONNECTION_STRING: observability.outputs.insightsConnectionString
    }
  }
}

module photos 'modules/photos.bicep' = {
  name: 'photos'
  params: {
    location: location
    storageName: storageName
    appOrigin: 'https://${appNames.gateway}.${environment.outputs.defaultDomain}'
    gatewayPrincipalId: gatewayIdentity.principalId
    mediaPrincipalId: mediaIdentity.principalId
  }
}

module messaging 'modules/messaging.bicep' = {
  name: 'messaging'
  params: {
    location: location
    namePrefix: namePrefix
    storageAccountName: photos.outputs.name
    mediaPrincipalId: mediaIdentity.principalId
    recipesPrincipalId: recipesIdentity.principalId
  }
}

module media 'modules/media.bicep' = {
  name: 'media'
  params: {
    location: location
    namePrefix: namePrefix
    identityId: mediaIdentity.id
    identityClientId: mediaIdentity.clientId
    storageAccountName: photos.outputs.name
    blobEndpoint: photos.outputs.blobEndpoint
    serviceBusNamespace: messaging.outputs.fullyQualifiedNamespace
    insightsConnectionString: observability.outputs.insightsConnectionString
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    namePrefix: namePrefix
    insightsId: observability.outputs.insightsId
    serviceBusNamespaceId: messaging.outputs.namespaceId
    uploadTopicId: messaging.outputs.uploadTopicId
    emails: budgetContactEmails
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  params: { amount: budgetAmount, startDate: budgetStartDate, contactEmails: budgetContactEmails }
}

@description('The app\'s address; add https://<this>/.auth/login/aad/callback to the app registration\'s redirect URIs.')
output gatewayFqdn string = gateway.outputs.fqdn

@description('The gateway revision this deployment made; CI moves the traffic to it once it passed the checks.')
output gatewayRevision string = gateway.outputs.revisionName

@description('Where to check the new gateway revision before it gets traffic: its "next" label, or the app itself when it got all traffic at once.')
output gatewayCandidateUrl string = gateway.outputs.waitsForSwitch
  ? 'https://${appNames.gateway}---next.${environment.outputs.defaultDomain}'
  : 'https://${gateway.outputs.fqdn}'

@description('Database user names for db/manual/grants.sql.')
output sqlUsers array = [for name in deployables: 'id-${namePrefix}-${name}']

@description('The Media function app, for the code deployment in CI.')
output mediaFunctionName string = media.outputs.name

@description('For the federated credential of the sign-in app registration (managedIdentity mode).')
output gatewayIdentityClientId string = gatewayIdentity.clientId

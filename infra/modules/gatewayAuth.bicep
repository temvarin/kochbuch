// Container Apps' built-in authentication (Easy Auth) on the gateway, with Microsoft Entra, like kassensturz's App
// Service. Everyone may load the app (AllowAnonymous); the gateway itself refuses the API to non-members.
// Which credential the app registration uses is decided in main.bicep (`authCredential`, docs/adr/0010).

param appName string
param tenantId string
param clientId string

@description('Name of the Container App secret that holds the credential setting (see main.bicep).')
param secretName string

@description('Lifetime of a sign-in; matches EasyAuth.SessionLifetime in the gateway.')
param sessionLifetime string = '30.00:00:00'

resource app 'Microsoft.App/containerApps@2025-01-01' existing = {
  name: appName
}

resource auth 'Microsoft.App/containerApps/authConfigs@2025-01-01' = {
  parent: app
  name: 'current'
  properties: {
    platform: { enabled: true }
    globalValidation: {
      // The app must load without a sign-in (offline-first PWA); /api is guarded by the gateway's Member policy.
      unauthenticatedClientAction: 'AllowAnonymous'
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: clientId
          clientSecretSettingName: secretName
          openIdIssuer: '${environment().authentication.loginEndpoint}${tenantId}/v2.0'
        }
      }
    }
    login: {
      cookieExpiration: {
        convention: 'FixedTime'
        timeToExpiration: sessionLifetime
      }
      preserveUrlFragmentsForLogins: true
    }
  }
}

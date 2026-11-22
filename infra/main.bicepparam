// Values for infra/main.bicep. Nothing personal or secret is written here: ids and addresses come from environment
// variables, which CI fills from GitHub variables and secrets (docs/deployment.md, step 5). The image tag comes from
// the command line: -p imageTag=<commit SHA>.
using './main.bicep'

param registry = 'ghcr.io/temvarin'
param imageTag = readEnvironmentVariable('KOCH_IMAGE_TAG', 'latest')
param gatewayRevisionSuffix = readEnvironmentVariable('KOCH_GATEWAY_REVISION_SUFFIX', '')
param gatewayStableRevision = readEnvironmentVariable('KOCH_GATEWAY_STABLE_REVISION', '')
param members = readEnvironmentVariable('KOCH_MEMBERS')
param signInClientId = readEnvironmentVariable('KOCH_SIGNIN_CLIENT_ID')
param authCredential = readEnvironmentVariable('KOCH_AUTH_CREDENTIAL', 'managedIdentity')
param signInClientSecret = readEnvironmentVariable('KOCH_SIGNIN_CLIENT_SECRET', '')
param sqlServerName = readEnvironmentVariable('KOCH_SQL_SERVER')
param sqlDatabaseName = readEnvironmentVariable('KOCH_SQL_DATABASE')
param budgetStartDate = readEnvironmentVariable('KOCH_BUDGET_START', '2026-12-01')
param budgetContactEmails = split(readEnvironmentVariable('KOCH_BUDGET_EMAILS'), ',')

using './main.bicep'

param registry = 'ghcr.io/temvarin'
param imageTag = readEnvironmentVariable('KOCH_IMAGE_TAG', 'latest')
param members = readEnvironmentVariable('KOCH_MEMBERS')
param signInClientId = readEnvironmentVariable('KOCH_SIGNIN_CLIENT_ID')
param budgetStartDate = readEnvironmentVariable('KOCH_BUDGET_START', '2026-12-01')
param budgetContactEmails = split(readEnvironmentVariable('KOCH_BUDGET_EMAILS'), ',')

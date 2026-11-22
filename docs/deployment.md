# Deployment: one-time setup

After this setup, every push to `main` is deployed by CI (`.github/workflows/ci.yml`):
1. Build and push the images to GHCR.
2. Apply `db/<service>/*.sql`.
3. `az deployment group create` with `infra/main.bicep`. The gateway's new revision starts with no traffic, at the
   label `next` (docs/adr/0009).
4. Smoke-check the new gateway revision at its `next` address, then move all traffic to it.

The setup itself is done once, by hand, in this order. It needs the Azure CLI (`az login`) and the Entra admin of kassensturz's SQL server.

## 0. Check the subscription (Azure for Students)
```powershell
az policy assignment list --query "[].{name:displayName, params:parameters}" -o jsonc   # allowed regions
az provider register -n Microsoft.App; az provider register -n Microsoft.OperationalInsights
az provider show -n Microsoft.App --query "resourceTypes[?resourceType=='managedEnvironments'].locations" -o tsv
az functionapp list-flexconsumption-locations -o table                                   # needed from phase 4 on
```
Pick a region that appears in both lists. kassensturz uses `swedencentral`.

If Flex Consumption isn't offered there, Media falls back to the Windows Consumption plan (phase 4).

## 1. Resource group and first deployment
```powershell
az group create -n rg-kochbuch -l swedencentral
$env:KOCH_MEMBERS = "<object id 1>,<object id 2>"   # the same ids as kassensturz's Geld__Members
$env:KOCH_SIGNIN_CLIENT_ID = "<from step 2>"
$env:KOCH_SQL_SERVER = "<kassensturz SQL server name, without .database.windows.net>"
$env:KOCH_SQL_DATABASE = "<kassensturz database name>"
$env:KOCH_BUDGET_EMAILS = "<your mail>"
$env:KOCH_IMAGE_TAG = "<a tag pushed by CI, or build and push once by hand>"
az deployment group create -g rg-kochbuch -f infra/main.bicep -p infra/main.bicepparam
```
Write down the outputs: `gatewayFqdn`, `gatewayIdentityClientId` and `sqlUsers`.

The images must exist and be **public** in GHCR:
- The first CI run pushes them. Before that, `-p imageTag=` can point at any public image to get the infrastructure up.
- On GitHub, open Packages → kochbuch-gateway / kochbuch-recipes → Package settings → Change visibility → Public.

## 2. Sign-in app registration (Entra)
1. Entra ID → App registrations → New: `kochbuch`, single tenant.
2. Redirect URI (Web): `https://<gatewayFqdn>/.auth/login/aad/callback`.
3. Under Authentication, tick **ID tokens**.
4. Credential, depending on `authCredential` (docs/adr/0010, decided on this first deploy):
   - **managedIdentity** (default):
     - Certificates & secrets → Federated credentials → Add → *Managed identity* → `id-kochbuch-gateway`.
     - Sign in once on a phone to confirm it works on Container Apps.
   - **clientSecret** (fallback):
     - Create a client secret and set `KOCH_AUTH_CREDENTIAL=clientSecret` and `KOCH_SIGNIN_CLIENT_SECRET`.
     - Put a reminder in the calendar to renew it before it expires.
5. The partner is already a guest in the tenant (from kassensturz). Nothing to do there.

## 3. Database grants (by hand, once)
In the Azure portal: SQL database (kassensturz's) → Query editor, signed in as the Entra admin. Run `db/manual/grants.sql`.

It creates:
- the `recipes` schema;
- the `id-kochbuch-recipes` user (rows of `recipes` only);
- the `kochbuch-deploy` user (DDL in `recipes` only).

The user names must match the `sqlUsers` output and the deploy app registration's name (step 4).

## 4. Deploy identity for GitHub Actions (OIDC, no stored password)
1. App registration `kochbuch-deploy`. Federated credentials for repository `temvarin/kochbuch`:
   - entity *Branch* `main` (deploys);
   - entity *Pull request* (what-if previews).
2. Roles on `rg-kochbuch`:
   - **Contributor** for deploys;
   - **Reader** would be enough for previews, but one app registration has one set of roles. Use a second registration if pull requests should be read-only.
3. Bicep assigns data roles (Blob, Service Bus, since phase 4), so the deploy identity also needs **Role Based Access
   Control Administrator** on `rg-kochbuch`, with the condition "allow only these roles": Storage Blob Data Owner /
   Contributor / Reader, Storage Blob Delegator, Azure Service Bus Data Sender / Receiver.

## 5. GitHub settings
- **Secrets:** `AZURE_CLIENT_ID` (kochbuch-deploy), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `KOCH_MEMBERS`, `KOCH_BUDGET_EMAILS`, optionally `KOCH_SIGNIN_CLIENT_SECRET`.
- **Variables:** `AZURE_RESOURCE_GROUP=rg-kochbuch`, `KOCH_SIGNIN_CLIENT_ID`, `KOCH_AUTH_CREDENTIAL`, `KOCH_SQL_SERVER`, `KOCH_SQL_DATABASE`, `KOCH_BUDGET_START` (first day of a coming month, `yyyy-MM-01`).

## Rollback
The gateway: the previous revision is still there, labelled `stable`, with no traffic. Switch back at once:
```powershell
az containerapp ingress traffic set -n kochbuch-gateway -g rg-kochbuch --label-weight stable=100 next=0
```
Everything else: re-run the "Deploy and check" job of an earlier run. It redeploys that commit's image tag and
infrastructure.

The database scripts only ever add to the schema, so an older revision still runs against a newer schema.

## Monitoring
- **Alerts** go to `KOCH_BUDGET_EMAILS`, from the action group `ag-kochbuch`. There are three: requests failed, dead
  letters in a queue, uploads Event Grid couldn't deliver. Each mail says where to look.
- **Queries:** in the Log Analytics workspace `log-kochbuch` → Logs → Queries → category "kochbuch". The files are in
  `infra/queries`; start with "failures", then "trace" with an operation id from it.
- **Live:** Application Insights `appi-kochbuch` → Transaction search, or the application map.

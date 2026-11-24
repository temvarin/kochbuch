# Deployment: one-time setup

After this setup, every push to `main` is deployed by CI (`.github/workflows/ci.yml`):
1. Build and push the images to GHCR.
2. Apply `db/<service>/*.sql`.
3. `az deployment group create` with `infra/main.bicep`. The gateway's new revision starts with no traffic, at the
   label `next` (docs/adr/0009).
4. Smoke-check the new gateway revision at its `next` address, then move all traffic to it.

The setup itself is done once, by hand, in the order below. It needs the Azure CLI (`az login`) and the Entra admin
of kassensturz's SQL server.

The order matters:
- The first deployment needs the sign-in app's client id, so the registration comes first.
- The registration's redirect URI needs the gateway's address, which only exists after that deployment, so it is
  completed afterwards.

## 1. GitHub repository and the images
1. Create the repository `temvarin/kochbuch` and push `main`.
2. CI's "Deploy and check" job builds and pushes the three images before it signs in to Azure. On this first run it
   then fails at the Azure sign-in. That is expected: the images exist, tagged with the commit SHA.
3. On GitHub, open Packages → `kochbuch-gateway`, `kochbuch-recipes` and `kochbuch-shopping` → Package settings →
   Change visibility → **Public**. Azure pulls them without a password.

## 2. Check the subscription (Azure for Students)
```powershell
az policy assignment list --query "[].{name:displayName, params:parameters}" -o jsonc   # allowed regions
az provider register -n Microsoft.App; az provider register -n Microsoft.OperationalInsights
az provider show -n Microsoft.App --query "resourceTypes[?resourceType=='managedEnvironments'].locations" -o tsv
az functionapp list-flexconsumption-locations -o table
```
Pick a region that appears in both lists. kassensturz uses `swedencentral`.

If Flex Consumption isn't offered there, Media falls back to the Windows Consumption plan: change `infra/modules/media.bicep` before the first deployment.

## 3. Sign-in app registration (Entra), first half
1. Entra ID → App registrations → New: `kochbuch`, single tenant. Leave the redirect URI empty for now.
2. Under Authentication, tick **ID tokens**.
3. Write down its **Application (client) id**.

## 4. First deployment
```powershell
az group create -n rg-kochbuch -l swedencentral
$env:KOCH_MEMBERS = "<object id 1>,<object id 2>"   # the same ids as kassensturz's Geld__Members
$env:KOCH_SIGNIN_CLIENT_ID = "<client id from step 3>"
$env:KOCH_SQL_SERVER = "<kassensturz SQL server name, without .database.windows.net>"
$env:KOCH_SQL_DATABASE = "<kassensturz database name>"
$env:KOCH_BUDGET_EMAILS = "<your mail>"
$env:KOCH_BUDGET_START = "<first day of a coming month, yyyy-MM-01>"
$env:KOCH_IMAGE_TAG = "<the commit SHA CI pushed in step 1: git rev-parse HEAD>"
az deployment group create -g rg-kochbuch -f infra/main.bicep -p infra/main.bicepparam
```
Write down the outputs: `gatewayFqdn`, `gatewayIdentityClientId` and `sqlUsers`.

The recipes and shopping services can't reach the database until step 6. Until then, the app loads but syncing fails.

## 5. Sign-in app registration, second half
1. Authentication → Add a platform → Web. Redirect URI: `https://<gatewayFqdn>/.auth/login/aad/callback`.
2. Credential, depending on `authCredential` (docs/adr/0010, decided here):
   - **managedIdentity** (default):
     - Certificates & secrets → Federated credentials → Add → *Managed identity* → `id-kochbuch-gateway`.
     - Open `https://<gatewayFqdn>` on a phone and sign in once to confirm it works on Container Apps.
   - **clientSecret** (fallback, if that sign-in fails):
     - Create a client secret, set `KOCH_AUTH_CREDENTIAL=clientSecret` and `KOCH_SIGNIN_CLIENT_SECRET`, and run the
       deployment from step 4 again.
     - Put a reminder in the calendar to renew it before it expires.
3. The partner is already a guest in the tenant (from kassensturz). Nothing to do there.

## 6. Database grants (by hand, once)
In the Azure portal: SQL database (kassensturz's) → Query editor, signed in as the Entra admin. Run `db/manual/grants.sql`.

It creates:
- the `recipes` and `shopping` schemas;
- the `id-kochbuch-recipes` and `id-kochbuch-shopping` users (rows of their own schema only, denied the others);
- the `kochbuch-deploy` user (tables in `recipes` and `shopping` only).

The user names must match the `sqlUsers` output and the deploy app registration's name (step 7).

## 7. Deploy identity for GitHub Actions (OIDC, no stored password)
1. App registration `kochbuch-deploy`. Federated credentials for repository `temvarin/kochbuch`:
   - entity *Branch* `main` (deploys);
   - entity *Pull request* (what-if previews).
2. Roles on `rg-kochbuch`:
   - **Contributor** for deploys;
   - **Reader** would be enough for previews, but one app registration has one set of roles. Use a second registration if pull requests should be read-only.
3. Bicep assigns data roles (Blob, Service Bus), so the deploy identity also needs **Role Based Access
   Control Administrator** on `rg-kochbuch`, with the condition "allow only these roles": Storage Blob Data Owner /
   Contributor / Reader, Storage Blob Delegator, Azure Service Bus Data Sender / Receiver.

## 8. GitHub settings, then the first CI deploy
- **Secrets:** `AZURE_CLIENT_ID` (kochbuch-deploy), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `KOCH_MEMBERS`, `KOCH_BUDGET_EMAILS`, optionally `KOCH_SIGNIN_CLIENT_SECRET`.
- **Variables:** `AZURE_RESOURCE_GROUP=rg-kochbuch`, `KOCH_SIGNIN_CLIENT_ID`, `KOCH_AUTH_CREDENTIAL`, `KOCH_SQL_SERVER`, `KOCH_SQL_DATABASE`, `KOCH_BUDGET_START` (the same date as in step 4).

Then re-run the failed run from step 1 (Actions → the run → Re-run all jobs). It applies the database scripts,
deploys, deploys the Media function, smoke-checks the gateway and switches traffic to it. From now on every push to
`main` does the same.

**Check it end to end:** open `https://<gatewayFqdn>` on both phones and sign in. Edit a recipe on one phone while it
is offline, sync both phones, and check that the other phone shows the change. Add a photo and check it appears.

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

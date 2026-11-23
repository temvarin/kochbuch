# Kochbuch

*Rezepte mit Fotos, Zutaten und Schritten – und eine Einkaufsliste, die Mengen aus mehreren Rezepten zusammenrechnet.*

A recipe PWA for a two-person household:
- Both phones share the same recipes, synced like [kassensturz](https://github.com/temvarin/kassensturz).
- Pick recipe 1 and recipe 2 and get one shopping list: 500 g + 300 g Kartoffeln = 800 g.

The fourth app in the series after 33-von-33, fallstrick and kassensturz. This one is built **as microservices on Azure on purpose, as a learning goal**. [ADR-0001](docs/adr/0001-microservices-as-a-learning-goal.md) explains why, where the books disagree, and when to merge services back.

> **Status: all seven phases built; not yet deployed to Azure.**
>
> Working:
> - recipes with photos, ingredients and steps, scaled to any number of servings, offline, synced between both phones
> - one shopping list: a recipe goes onto it from its page, matching ingredients add up (500 g + 300 g = 800 g),
>   ticked off on both phones
> - a cooking view, one step per screen with the screen kept on; ingredients typed as one line, "500 g Kartoffeln"
> - photos: uploaded straight from the phone to Blob Storage, turned into WebP renditions by an Azure Function (camera
>   metadata and GPS position removed), served by the gateway and kept offline by the service worker
> - photo clean-up: Recipes publishes "attached"/"detached" through a transactional outbox, and Media deletes photos no
>   recipe has shown for 30 days
> - monitoring: OpenTelemetry into Application Insights with one trace from the phone's sync to Media, saved KQL
>   queries, three alerts by mail, and gateway releases checked at a `next` label before they get traffic
>
> - two phones editing one recipe keep both edits: a three-way merge per part (title, each ingredient line, steps, …)
> - browser tests of the whole household: two phones, offline edits, a shared shopping list, accessibility checks

## Architecture
```mermaid
flowchart LR
  phone["Phone (PWA)"]
  subgraph aca["Container Apps environment"]
    gw["Koch.Gateway<br/>Easy Auth, YARP, serves the PWA"]
    rec["Koch.Recipes"]
    shop["Koch.Shopping"]
  end
  sql[("kassensturz's Azure SQL DB<br/>schemas recipes, shopping")]
  blob[("Blob Storage<br/>uploads/, photos/")]
  eg["Event Grid<br/>system topic"]
  sbu[["Service Bus queue<br/>photo-uploads"]]
  sbl[["Service Bus queue<br/>photo-lifecycle"]]
  media["Koch.Media<br/>Azure Function, Flex"]
  phone -- HTTPS --> gw
  gw -- "/api/recipes" --> rec
  gw -- "/api/shopping" --> shop
  rec --> sql
  shop --> sql
  rec -- "outbox relay" --> sbl --> media
  phone -- "PUT with a write-only SAS" --> blob
  blob -- BlobCreated --> eg --> sbu --> media
  media -- "WebP renditions, tags" --> blob
  gw -- "reads photos/" --> blob
```
Every app sends traces, metrics and logs to Application Insights with OpenTelemetry ([ADR-0009](docs/adr/0009-observability-alerts-and-staged-releases.md)).
One trace follows a recipe saved with a photo across the asynchronous hop:

```mermaid
sequenceDiagram
  participant P as Phone
  participant G as Gateway
  participant R as Recipes
  participant D as SQL
  participant S as Service Bus
  participant M as Media
  P->>G: POST /api/recipes/sync
  G->>R: POST /sync (traceparent)
  R->>D: recipe row and outbox event with its traceparent, one transaction
  R-->>G: synced recipes
  G-->>P: synced recipes
  Note over R: relay: after the request, at start, every 5 minutes
  R->>S: CloudEvent with traceparent and Diagnostic-Id
  S->>M: delivered at least once
  Note over M: span "apply photo event" in the same trace
```

| Decision | ADR |
|---|---|
| Microservices as a learning goal; few, justified boundaries | [0001](docs/adr/0001-microservices-as-a-learning-goal.md) |
| One gateway signs in; services trust the user it names | [0002](docs/adr/0002-gateway-sign-in-and-trust.md) |
| kassensturz's database, one schema and one identity per service | [0003](docs/adr/0003-shared-database-schema-per-service.md) |
| Recipe sync: kassensturz's protocol, one row per recipe, ingredient ids from their names | [0004](docs/adr/0004-recipe-sync.md) |
| Shopping lists built on the phone, one item per contribution, a shared kernel | [0005](docs/adr/0005-shopping-lists.md) |
| Photos: valet-key upload, Event Grid → Service Bus → Function, served by the gateway | [0006](docs/adr/0006-photo-pipeline.md) |
| Service Bus Basic queues, no topics; Event Grid for storage events | [0007](docs/adr/0007-service-bus-basic.md) |
| Photo clean-up: transactional outbox, events ordered by sequence, blob tags, daily sweep | [0008](docs/adr/0008-outbox-and-photo-lifecycle.md) |
| Observability: OpenTelemetry into Application Insights, one trace across the outbox, three alerts, staged gateway releases | [0009](docs/adr/0009-observability-alerts-and-staged-releases.md) |
| Easy Auth signs in with a federated managed identity, a client secret as fallback; no Key Vault (proposed until the first deploy) | [0010](docs/adr/0010-sign-in-credential.md) |
| Cost model for Azure for Students; the Azure services and tools deliberately not used, Aspire included | [0011](docs/adr/0011-cost-model-and-what-is-not-used.md) |
| Concurrent recipe edits merged part by part with the phone's base; browser tests of the household; no topic yet | [0012](docs/adr/0012-recipe-merge-and-browser-tests.md) |
| No shop aisles, and so no ingredient catalogue: lists show open items by name | [0013](docs/adr/0013-no-aisles-no-ingredient-catalogue.md) |
| The UI: photo first, one shopping list, cooking view, ingredients typed as one line | [0014](docs/adr/0014-ui-photo-first-one-list.md) |

### Projects
Read from the outside in: what runs, what the runnables share, then tests and infrastructure.

| Project | What |
|---|---|
| **Deployables** | |
| `src/Koch.App` | Blazor WebAssembly PWA, German UI, hand-written CSS; served by the gateway. `Recipes/` (tiles, recipe page, cooking view, form with `IngredientText`), `Shopping/` (the one list), `Sync/`, `Shell/` |
| `src/Koch.Gateway` | The only public app: Easy Auth handler, Member policy, YARP proxy, security headers, photo upload links and serving |
| `src/Koch.Recipes` | Recipes service: `POST /sync`; `Storage/` maps the book to rows; `Outbox/` relays photo events to Service Bus |
| `src/Koch.Shopping` | Shopping service: `POST /sync` for the list's items |
| `src/Koch.Media` | Azure Function (Flex Consumption): turns uploads into renditions, applies photo events as blob tags, sweeps detached photos daily |
| **Rules, shared by the app and a service** | |
| `src/Koch.Kernel` | Shared vocabulary and sync machinery: units, quantities, `Synced<TBook, TChanges>` |
| `src/Koch.Recipes.Domain` | Recipes, ingredient identity (`IngredientKey`), the sync book and its merge (`RecipeMerge`), the phone's local store |
| `src/Koch.Shopping.Domain` | The shopping list: contributions, combining amounts, lines, ticking, sync |
| `src/Koch.Media.Imaging` | Photo work: validate, turn upright, WebP at 400 and 1200 px, nothing of the original kept |
| **Contracts: what one deployable publishes for others** | |
| `src/Koch.*.Contracts`, `src/Koch.Recipes.Events` | One small library per producer and purpose (Clean Code ch20, CRP): wire shapes, the photo path layout, photo events |
| **Hosting plumbing** | |
| `src/Koch.ServiceDefaults` | What every service shares: the forwarded user, the synced rows in SQL with their outbox |
| `src/Koch.Telemetry` | How the HTTP apps report: OpenTelemetry into Application Insights |
| **Tools** | |
| `tools/Koch.DbDeploy` | Applies `db/<service>/*.sql`, locally and in CI |
| `tools/Koch.DevPhotos` | Development only: prepares Azurite and processes uploads the way Event Grid and Media do in Azure |
| **Tests** | |
| `tests/Koch.*.Domain.Tests` | Rules, randomized multi-phone sync, the merge, contract samples of local storage and the sync API |
| `tests/Koch.Recipes.Tests`, `tests/Koch.Shopping.Tests` | The services: row store contract on SQL and memory, sync API, outbox, events, row samples |
| `tests/Koch.App.Tests`, `tests/Koch.Gateway.Tests`, `tests/Koch.Media.Tests` | bUnit pages against the real service in memory; auth, proxy and spoofed headers; renditions and photo events |
| `tests/Koch.Architecture.Tests` | Which project may use which, KQL checked against the tables, every path and ADR cited in docs and comments exists |
| `tests/Koch.E2E` | Playwright: the real gateway and app on a real port, the services in memory, two phones as two browser contexts, axe checks |
| `tests/Koch.Testing` | Test helpers shared by the test projects: the memory row store |
| **Infrastructure and data** | |
| `infra/` | Bicep: observability with saved queries, identities, Container Apps environment, apps (the gateway in staged revisions), Easy Auth, photos, messaging, alerts, budget alarm |
| `infra/queries` | KQL: one trace end to end, failures, latency percentiles, photo events, outbox relay, ingestion against the cap |
| `db/` | Idempotent SQL per service schema; `db/manual/grants.sql` once by hand |

## Development
Needs the .NET 10 SDK (`global.json`) and SQL Server LocalDB (kassensturz's dev database `Kassensturz`).

1. Put the schema into the local database once:
   ```powershell
   $env:KOCH_DB_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=Kassensturz;Integrated Security=true'
   dotnet run --project tools/Koch.DbDeploy -- db/recipes db/shopping
   ```
2. Run the services and the gateway and open http://localhost:8091. In Development the gateway signs you in as `dev`.
   ```powershell
   dotnet run --project src/Koch.Recipes      # http://localhost:8092
   dotnet run --project src/Koch.Shopping     # http://localhost:8093
   dotnet run --project src/Koch.Gateway      # http://localhost:8091
   ```
   Photos locally need the storage emulator and the stand-in for Event Grid + Media (Node.js for Azurite):
   ```powershell
   npx -y azurite@3 --location .azurite --skipApiVersionCheck
   ```
   ```powershell
   dotnet run --project tools/Koch.DevPhotos
   ```
3. Tests (SQL tests use LocalDB, or `KOCH_TEST_SQL`; the browser tests use the installed Edge):
   ```powershell
   dotnet test -c Release
   ```
4. Infrastructure:
   ```powershell
   bicep build infra/main.bicep; bicep lint infra/main.bicep
   ```

## Deployment
CI deploys every push to `main`. The one-time Azure, Entra, SQL and GitHub setup is in [docs/deployment.md](docs/deployment.md).

Cost target: **under $1 a month** on top of kassensturz:
- scale-to-zero Container Apps and a Flex Consumption Function inside their free grants
- Service Bus Basic, Event Grid's free operations, a few cents of Blob Storage
- public GHCR images instead of ACR
- a log cap
- no extra database

A $3 budget alarm watches it; the three alerts add about $0.40.

## License
PolyForm Noncommercial 1.0.0, like the sibling apps (add `LICENSE.md` before publishing).

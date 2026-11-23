# ADR-0011: Cost model for Azure for Students, and the Azure services and tools deliberately not used

**Status:** Accepted (2026-11-22). The figures are estimates from the plan; check them in Cost Management after the
first full month.

## Context
The subscription is Azure for Students: $100 a year, of which kassensturz's SQL Basic database takes about $60. kochbuch
must therefore run on about **$0–1 a month**. For two people it is idle almost all the time: a few syncs a day, a few
photos a week. Paying for idle capacity is the main way to overspend.

## Decision
### Every part is billed per use, and idle costs nothing

| Part | How it stays cheap | Estimate a month |
|---|---|---|
| Gateway, Recipes, Shopping (Container Apps, Consumption) | 0 replicas when idle, at most 1, 0.25 vCPU / 0.5 GiB; inside the monthly free grant (180,000 vCPU-s, 360,000 GiB-s, 2 million requests) | $0 |
| Media (Functions, Flex Consumption) | On demand only, no always-ready instances; inside the free grant | $0 |
| Service Bus Basic, two queues | $0.05 per million operations | ~$0.01 |
| Event Grid system topic | First 100,000 operations free | $0 |
| Blob Storage, LRS | A few GB of WebP renditions; uploads deleted after processing | ~$0.05 |
| Log Analytics and Application Insights | 30 days' retention, daily cap 0.1 GB (inside the free 5 GB a month) | $0 |
| Alerts (ADR-0009) | Three metric alerts, four series at $0.10 | ~$0.40 |
| Images | Public GitHub Container Registry | $0 |
| Database | kassensturz's, one schema per service (ADR-0003) | $0 extra |
| **Total** | | **≈ $0.50** |

- **A $3 budget alarm** on the resource group mails at 50 % and 100 % (`infra/modules/budget.bicep`). It catches a
  mistake, for example a replica that stays up or a log loop, within the month.
- **The price of these choices is latency.** The first request after idle starts a replica (a few seconds), and an
  event saved just before Recipes scales to zero waits for the next request (ADR-0008). The app works offline first,
  so a cold start delays the sync, not the person.

### Azure services not used, and why

| Service | Why not |
|---|---|
| Cosmos DB, PostgreSQL | The database is given: kassensturz's Azure SQL, at no extra cost (ADR-0003). |
| Azure Cache for Redis | Nothing to cache: each sync reads a household's rows once. |
| AKS | A cluster for three small apps is far more to run and pay for than Container Apps (Newman ch1 p.27). |
| API Management | Can't serve the PWA; YARP in the gateway does the routing for free (ADR-0002). |
| Static Web Apps | Not offered in the allowed region. And sign-in, the API and the app must share one origin (ADR-0002). |
| App Configuration | A handful of settings, all set by Bicep as environment variables. |
| Service Bus Standard, topics | No second consumer of any event; about $10 a month (ADR-0007, ADR-0012). |
| Azure Container Registry | About $5 a month; public GHCR images are free. |
| Key Vault | No secret to keep with the federated managed identity (ADR-0010). |
| Front Door, CDN | Two users; the gateway serves photos with long cache lifetimes, and the service worker keeps them. |

### Tools not used: .NET Aspire
The plan had an Aspire AppHost for running everything locally, and integration tests on it with Azurite and the
Service Bus emulator. It was not built:
- Running locally is three `dotnet run` commands plus Azurite and `tools/Koch.DevPhotos` for photos (README). An
  orchestration project would add a dependency for that alone.
- The browser tests (ADR-0012) run the whole app in one test process without it.

**The cost of leaving it out:** the adapters to Azure SDKs have only been run against in-memory fakes:
- the blob-tag conditions and search by tags;
- the Service Bus publisher;
- the Function triggers.

The first deployment is their first real run. If they break there, an integration test against Azurite (which supports
blob tags) is the next step, with or without Aspire.

## Consequences
- The cost stays near zero only while the replicas really scale to zero and logs stay under the cap. The budget alarm
  and `infra/queries/ingestion.kql` show when that stops being true.
- If the free grants or prices change, this table is the place to update.

## Compliance
- `infra/modules/containerApp.bicep`: `minReplicas: 0`, `maxReplicas: 1`.
- `infra/modules/observability.bicep`: retention 30 days, daily cap.
- `infra/modules/budget.bicep`: the alarm.
- `infra/modules/alerts.bicep`: metric alerts only.

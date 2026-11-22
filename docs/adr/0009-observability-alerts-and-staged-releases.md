# ADR-0009: Observability: OpenTelemetry into Application Insights, one trace across the outbox, few alerts, staged gateway releases

**Status:** Accepted (2026-11-22)

## Context
Four deployables, two queues and a database now work together on one user action. A recipe saved with a photo crosses
the gateway, Recipes, SQL, the outbox, Service Bus and Media. When something goes wrong, the question is "where?", and
with logs in four places nobody can answer it. "Observability is optional" is the eleventh fallacy of distributed
computing (FOSA ch9 p.150). DDIA ch1 p.20 names tracing tools such as OpenTelemetry as the means: they record which
service called which, for what, and how long it took.

Before this ADR every app had `APPLICATIONINSIGHTS_CONNECTION_STRING` but sent nothing. Only the container console logs
reached Log Analytics.

What constrains the answer:
- One operator, two users, a few hundred requests a day. A self-hosted Jaeger or Grafana stack would be the biggest
  running thing in the system. Newman ch10 p.342 says to instrument from the start only if a managed service makes it
  easy, and here one does.
- The budget is under $1 a month (README, "Deployment"). The Log Analytics workspace has a daily cap of
  0.1 GB.
- **An alert should mean "look now".** Newman ch10 p.329-333: too many alerts hide the one that matters. A good alert is
  relevant, unique, timely, prioritised, understandable, diagnostic and advisory.

## Decision
1. **OpenTelemetry in the code, Application Insights as where it goes.**
   - The HTTP apps call `AddTelemetry(roleName)` from `Koch.Telemetry`, which uses Microsoft's Azure Monitor
     distribution of OpenTelemetry. ASP.NET Core, HttpClient (and so YARP), SqlClient and the Azure SDK record their own
     spans, metrics and logs. The W3C `traceparent` header carries a trace from the gateway into the services.
   - Media, a Function, uses the Functions worker's OpenTelemetry setup with the Azure Monitor exporter. `host.json`
     switches the Functions host to `telemetryMode: OpenTelemetry`.
   - The classic Application Insights SDK was rejected: it ties the code to one vendor and is in maintenance. Newman
     ch10 p.326 recommends tooling that supports OpenTelemetry so the backend can change.
   - **Every trace is kept, whole** (`SamplingRatio = 1`). At this volume, sampling would only throw away the one trace
     you need. If traffic ever grows, the daily cap limits the cost: data is lost, not money.
   - `/healthz` is not traced. The probe runs every 30 seconds while a replica runs, and would outnumber real requests.
   - Without a connection string (locally, in tests) nothing is set up.
   - `Koch.Telemetry` is its own small assembly so the gateway gets it without the services' row store (Clean Code
     ch20, the Common Reuse Principle).
2. **One trace across the asynchronous hop.**
   - `SqlDocumentRows` stores the current `traceparent` in the outbox row with the event (`db/recipes/003-outbox-trace.sql`).
   - The relay publishes it twice:
     - as the CloudEvents distributed-tracing extension `traceparent`, for any consumer (an optional attribute: samples
       `v2-2026-11-22`);
     - as Service Bus' own `Diagnostic-Id` property. The SDK fills that from the sender's span only if it is not set,
       so it would otherwise point at the relay instead of at the request.
   - Media starts its span "apply photo event" in the trace of the sync request and links the function invocation. One
     trace then shows the phone's sync, the gateway, Recipes, SQL, the outbox, Service Bus and Media.
   - An event without a trace, or with an invalid one, is applied all the same.
   - The relay's rounds get a span of their own ("outbox relay"), so their database reads and the Service Bus send show
     up together.
   - **Not joined:** the upload path. The phone PUTs straight to Blob Storage, and BlobCreated carries no trace context.
     Uploads are correlated by photo id instead.
3. **Three alerts, all metric alerts, mailed to the budget alarm's addresses.**

   | Alert | Watches | What it means |
   |---|---|---|
   | Requests failed | Application Insights `requests/failed` > 0 in 15 min | A 5xx answer or a failed function run |
   | Dead letters | Service Bus `DeadletteredMessages` > 0, one series per queue | Ten failed deliveries: a photo not processed, or a clean-up not applied |
   | Undelivered uploads | Event Grid `DeadLetteredCount` > 0 | An upload never reached the queue |

   - Each alert's description says what broke and which query or tool to open. Each resolves itself, with a second
     mail.
   - **Metric alerts cost $0.10 per series a month; log-query alerts cost $0.50 or more each.** Four series cost
     $0.40 a month.
   - **Not alerted:**
     - Outbox relay failures. Nothing is lost, and clean-up waits 30 days anyway (`outbox-relay.kql`).
     - Latency. Two users give too few requests for a percentile to mean anything within an hour.
     - Cold starts, which are expected.
   - Newman p.329: "Should this problem cause someone to be woken up at 3 a.m.?" None of these should; all are worth a
     mail.
4. **The queries are files**, in `infra/queries`:
   - one trace end to end
   - failures of the last day
   - latency percentiles and errors
   - photo events applied, with their delay after the sync
   - outbox relay rounds
   - ingestion against the cap

   Bicep saves them in the workspace (category "kochbuch"). `QueryTests` parses each query and type-checks it against
   the Application Insights tables, so a misspelt column fails CI instead of failing during an incident.
5. **Service-level objectives as aims, not alerts** (DDIA ch2 p.40-42 on percentiles, Newman ch10 p.327-329):
   - the median sync under 300 ms;
   - p95 under 2 s even with a cold start;
   - no 5xx.

   `latency.kql` measures them. There is no error budget: there is nobody to negotiate it with.
6. **Staged gateway releases with revision labels** (DDIA ch2 p.47: gradual rollouts and fast rollback; Newman ch10
   p.337: smoke tests after deployment, before release).
   - The gateway runs in multiple-revision mode. A deployment creates `kochbuch-gateway--r<run>-<attempt>` with 0 %
     traffic under the label `next`. The live revision keeps 100 % under `stable`.
   - CI runs its smoke checks against `https://kochbuch-gateway---next.<environment domain>`:
     - health
     - the start page
     - 401 on the API and on the proxied service paths

     Only then does it move the traffic. A failing check stops the job, and nobody ever reaches the new revision.
   - The previous revision stays active with no traffic and no replicas, which is free. Rollback is one command:
     `az containerapp ingress traffic set -n kochbuch-gateway -g <group> --label-weight stable=100 next=0`. Older
     revisions are deactivated.
   - **Only the gateway.** It serves the app and holds the sign-in, so a broken gateway breaks everything for everyone.
     The services are internal, stay single-revision, and are covered by the tests. Labels for them would need routing
     in the gateway to reach them.
   - The labelled address isn't a registered redirect URI, so nobody can sign in there. The checks need no sign-in.

## Consequences
- **Cost:**
  - Alerts are about $0.40 a month.
  - Telemetry is estimated at a few MB a day, inside the free 5 GB a month and well under the cap (`ingestion.kql`
    shows it).
  - The old gateway revision costs nothing at zero replicas.
- **Privacy:**
  - Traces hold URLs, recipe and photo ids, timings and the outbox's log lines.
  - SqlClient records no parameter values, so recipe contents and ingredients don't leave the database.
  - The forwarded user header is not recorded.
  - Retention is 30 days.
- The trace of a photo event can be long: Recipes may sleep for days before the relay runs (ADR-0008).
  `photo-events.kql` shows that delay. It is expected, not a fault.
- Synthetic transactions (Newman p.335-336) are limited to CI's smoke checks after each deployment. A scheduled one
  would need a test user in Entra and test data in the shared database; it is listed for phase 7.
- The alerts, queries and revision switch run for the first time on the first deployment after this ADR. Nothing here
  is checked against a real workspace except by the KQL type check.

## Compliance
- `Koch.Recipes.Tests`:
  - the row store contract (SQL and memory) keeps the trace an event was saved in, and none outside a trace;
  - a sync request's `traceparent` ends up on its photo event as a child span;
  - the CloudEvent and the Service Bus message carry the trace, and carry nothing when there was none;
  - the newest event samples are v2, with `traceparent`.
- `Koch.Media.Tests`:
  - every sample is read, its trace taken when present;
  - an invalid trace is ignored and the event still applied;
  - the span continues the request's trace and links the invocation, or belongs to the invocation when the event
    names no trace.
- `Koch.Gateway.Tests`: with Application Insights configured, the gateway starts and has a tracer.
- `Koch.Architecture.Tests`:
  - `Koch.Telemetry` depends on nothing of ours;
  - every query in `infra/queries` parses and uses only real tables and columns.
- CI: the smoke checks run against the new gateway revision before it gets traffic.

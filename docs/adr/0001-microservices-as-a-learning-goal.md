# ADR-0001: Microservices, as an explicit learning goal, with few boundaries

**Status:** Accepted (2026-11-22)

## Context
Kochbuch is a recipe and shopping-list PWA:
- two users (one household)
- one developer
- a domain that is new but well understood

The developer wants to learn how microservices are built and run on Azure. This is the fourth app in a row; the first three are monoliths.

The books disagree with microservices for a system like this:
- DDIA ch01 p.22: microservices mainly answer a people problem (teams that must move independently), and in a small company they are probably unnecessary overhead.
- Newman, *Building Microservices* 2e, ch1 "Whom They Might Not Work For" (p.31-32): brand-new products and small teams pay the "microservice tax" with the least to gain.
- FOSA ch11 p.176: a modular monolith suits small, new systems, and you move to distributed styles later.

Newman ch3 "Have a Goal" (p.71) still accepts microservices when the goal is written down and no simpler approach reaches it. Learning to design, deploy and operate a distributed system can only be done by building one.

## Decision
The system is a small set of deployables, each split off for a reason that would hold on its own merits as well:

| Deployable | Hosting | Why it is separate |
|---|---|---|
| **Gateway** (BFF) | Container App, external | The one public origin: serves the PWA, signs members in with Easy Auth, forwards to the services. The cookie and the same-origin CSRF defence need a single origin (ADR-0002). |
| **Recipes** | Container App, internal | The system of record for recipes and the ingredient catalogue. |
| **Shopping** | Container App, internal | Owns the lifecycle of a shopping list: create, sync, tick off, archive. Lists are snapshots built on the phone (ADR-0005). This is the thinnest service; see Consequences. |
| **Media** | Azure Functions (Flex Consumption) | Photo processing is asynchronous, CPU- and memory-heavy, bursty and allowed to fail on its own. Those runtime needs differ from interactive HTTP (FOSA ch07 p.96-99, quanta; Newman p.33, matching each service to the cloud runtime that suits it). |

Rules:
- Services talk over HTTP (gateway to service) or queues (to Media) only. They never use each other's code.
- Shared code is a small `*.Contracts` library per producer (Clean Code ch20, CRP p.413). Shared hosting plumbing lives in `Koch.ServiceDefaults`.
- There is no shared domain or entity library. FOSA ch14 p.216-218 calls that an antipattern, because every schema change would redeploy every service.
- Synchronous by default. Messaging only where the work is asynchronous by nature, which here means photos (FOSA ch19 p.364).

## Consequences
- **More moving parts than the app needs:** four deployables, two queues, distributed tracing. That is accepted as the price of the goal. Each part runs with scale-to-zero so it costs almost nothing (ADR-0011).
- **The Recipes, Shopping and kassensturz databases are one physical database** with a schema per service (ADR-0003). In FOSA's terms (ch07 p.98) they share an operational quantum.
- **Shopping is the weakest boundary.** If it never grows behaviour of its own, merge it into Recipes. That only means moving one module, because it shares no code with Recipes beyond contracts.
- **When to revisit (merge back):**
  - running the deployables costs more time than it teaches;
  - a feature regularly has to change two services at once (Newman ch2 p.38, cohesion);
  - cold starts noticeably hurt everyday use.

## Compliance
- `tests/Koch.Architecture.Tests`:
  - no deployable references another;
  - contracts reference nothing of ours;
  - the app knows the services only through their contracts;
  - `Koch.ServiceDefaults` holds no service code.
- Each new service adds itself to those rules.

## Notes
The design-KB notes behind this: `newman-microservices-adoption.md`, `monolith-vs-services-and-messaging.md`, `fosa-choosing-a-style.md`.

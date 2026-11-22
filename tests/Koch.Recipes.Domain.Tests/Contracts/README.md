# Data contracts of the Recipes service

Data that one version writes and another reads. Each folder holds samples of what released versions wrote, named
`v<version>-<yyyy-MM-dd>.json`. `ContractTests` (here) and `RowContractTests` (in Koch.Recipes.Tests) check that this
version writes exactly the newest sample and still reads every older one. Ported from kassensturz.

| Contract | Written by | Read by | Versioned by |
|---|---|---|---|
| `local` | the app, in localStorage | the app, after an update | `BookStore.Schema` |
| `sync-request` | the app | the service | `SyncRequestJson.CurrentProtocol` |
| `sync-response` | the service | the app | the protocol |
| `row-recipe`, `row-ingredient` (Koch.Recipes.Tests) | the service | the service, any later version: rows are never migrated | none: only additive changes |

## Rules
- **Never edit a sample** that was released. A test fails with `received.json` next to the sample when the written
  shape changes; if the change is intended, add that file as a new sample.
- **A new field** in a recipe or ingredient needs a new protocol: an older app would send the item without it and
  clear it on the service (items travel whole).
- Protocol 3 (2026-11-22) adds `recipes.bases`: changed recipes as the phone last got them, which the service merges
  with (docs/adr/0012). Left out when there are none. The local store keeps unanswered changes as a request, so it
  moved to schema 3 with it.
- **Rows** may only gain optional fields; every older row sample must keep reading.
- **local** gets a new schema and a migration in `BookStore.TryRead` when its shape changes.

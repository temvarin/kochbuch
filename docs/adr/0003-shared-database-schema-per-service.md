# ADR-0003: kassensturz's database, one schema and one identity per service

**Status:** Accepted (2026-11-22)

## Context
The owner chose to use the same Azure SQL database as kassensturz. That database is Basic tier: about $5 a month, 5 DTU, Sweden Central, on an Azure for Students subscription with ~$100 of credit a year.

A second database would cost another ~$5 a month. The free serverless offer pauses itself, so the first request after a pause waits.

Microservices normally avoid shared databases:
- FOSA ch18 p.333: data isolation.
- Newman ch2 p.46-48, common coupling: several services reading and writing the same records is the harmful case.

## Decision
- Each service gets its own schema (`recipes`, later `shopping`) in kassensturz's database. Its tables live only there.
- Each service logs in with its own user-assigned managed identity. Its SQL user has `SELECT/INSERT/UPDATE` on its own schema and `DENY` on `dbo` (`db/manual/grants.sql`). No service can read another's tables or kassensturz's.
- Tables follow kassensturz's proven shape: one JSON document per row, an integer version for compare-and-set, and system-versioned temporal history kept for 6 months.
- DDL: idempotent scripts in `db/<service>/`, applied by CI on each deploy (`tools/Koch.DbDeploy`). CI's deploy identity holds `ALTER` on the kochbuch schemas only. Users and grants are created **by hand** by the database admin. The deploy identity is never made SQL Entra admin, so this repository's CI can't touch kassensturz.

## Consequences
- $0 extra.
- Logical isolation is enforced by permissions, but physically one database means a shared failure domain and a shared 5 DTU budget with kassensturz. FOSA ch07 p.98 would count it as one quantum. Background work (outbox polling) must stay gentle.
- Moving a service to its own database later only changes the connection string, because nothing references another schema.

## Compliance
- `tests/Koch.Architecture.Tests`: only `Koch.Recipes.Storage` uses SqlClient.
- `tests/Koch.Recipes.Tests/DatabaseTests`: the scripts run against a real SQL Server, are idempotent, enforce JSON and keep history.
- Permissions: `grants.sql` is reviewed by hand; there is no automated check yet.

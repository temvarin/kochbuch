# ADR-0004: Recipe sync: kassensturz's protocol, one row per recipe, name-derived ingredient ids

**Status:** Accepted (2026-11-22)

## Context
Both phones edit the same recipes, often offline (in the kitchen, in the shop). Each phone is therefore a leader in
multi-leader replication, with lag of hours or days (DDIA ch06 p.220-221). kassensturz already has a sync model that
was tested this way:
- the phone keeps the last server-confirmed base and sends what changed since;
- changed items travel whole, so a repeated request does no harm;
- the server's arrival order decides between two changes, never the phone clocks;
- unanswered changes are resent as they are now.

kassensturz stores each synced document as one row. A recipe book holds many recipes, and each one changes on its own.

## Decision
- **Protocol: kassensturz's, generalised.** `ItemChanges<T>`, `SyncedBook` (port of `SyncedData`) and `SyncService` are
  ported, with recipes and catalogue ingredients as the item types. Everything is shared by both members, so the
  per-user check (412) is dropped.
- **One row per recipe and per ingredient** (`recipe:{id}`, `ingredient:{id}` in `recipes.Documents`):
  - One sync request is saved in **one transaction** (all or nothing).
  - A write whose JSON is unchanged is skipped, so a retried request adds no versions or history.
  - A removal writes a **tombstone** row. The other phone learns of it from the next full answer, and the temporal
    history still holds the recipe.
  - The service answers with the whole live book, which becomes the phone's new base.
- **Conflicts: whole recipe, last arrival wins** (DDIA p.224-225). Two people editing the same recipe at the same time is
  rare. The temporal history keeps the losing version, and `db/` can restore it.
- **Ingredient identity comes from the name.** The id is a UUID v8 from SHA-256 of the trimmed, lower-case name. When
  both phones add "Kartoffeln" offline, they create the *same* item, so the sync merges them instead of keeping two that
  the shopping list (phase 3) would not add up. Recipe lines name their ingredient rather than referencing an id, so no
  line can point at something that was removed.

## Consequences
- A lost update is still possible when both phones edit the same recipe between syncs. Accepted for now. Phase 7 plans
  a three-way merge per recipe part (title, each line, each step), with the base taken from temporal history
  (`notes/offline-sync-conflict-resolution.md`).
  - **Superseded in part by ADR-0012:** recipes are now merged part by part, with the base sent by the phone (protocol
    3). Last arrival wins only within one part.
- Every sync answer is the whole book. That is fine for a household (hundreds of recipes, well under 1 MB); paging or
  "changed since version N" can come later if needed.
- Renaming an ingredient in a recipe makes it a different ingredient for the shopping list. That is the intended meaning.

## Compliance
- `tests/Koch.Recipes.Domain.Tests/SyncTests`: convergence over 200 random histories of three phones (edits, removals,
  lost answers, requests that never arrive), lost answers, offline-added ingredients ending up once, last arrival wins.
- `tests/Koch.Recipes.Tests/RecipeStoreTests`: the same contract for SQL and memory (tombstones, unchanged writes,
  rollback of a failing request). `SyncApiTests`: 415 / 400 / 409, idempotency.
- `tests/Koch.App.Tests`: real pages against the real service in memory, with a lost answer and with offline-then-online.
- Contract samples (`tests/*/Contracts`): local storage, sync request and response, rows. This check found a real bug on
  the day it was written: the local store left out nulls that reading requires.

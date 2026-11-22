# ADR-0012: Three-way recipe merge, browser tests of the whole household, and no Service Bus topic yet

**Status:** Accepted (2026-11-22)

## Context
ADR-0004 kept a whole recipe as the unit of conflict: of two edits made from the same version, the one that reached the
service last replaced the other. The other phone's edit was lost silently (DDIA ch06 p.224-225), and only the temporal
history kept it. The typical case is common in a household:
- one person fixes an amount at the stove, offline;
- the other renames the dish or adds a step;
- both phones sync later.

Phase 7 of the plan also lists:
- browser tests of the whole system;
- a Service Bus Standard topic "when a second consumer exists".

## Decision
### 1. Merge concurrent recipe edits part by part (automatic, DDIA ch06 p.226-227)
- **Where the base comes from: the phone.** The phone already keeps the last book the service confirmed as its sync
  base (ADR-0004). With every changed recipe it sends that recipe's base (protocol 3, `recipes.bases`).
  - The plan's alternative was to read the base from the temporal history. It needs a version number on the phone and
    a history query per recipe, and it doesn't work for the memory store. With one service and phones that keep their
    base, the base is the causal context (`notes/offline-sync-conflict-resolution.md`).
- **Where the merge runs: in the row store, under the row's lock.**
  - `RowWrite.MergeWith` gets the JSON stored *now* and returns what to write. `SqlDocumentRows` calls it after
    `SELECT … WITH (UPDLOCK, HOLDLOCK)`, in the request's transaction.
  - Two requests for one recipe therefore merge one after the other, each with the latest version. This is the
    read-modify-write remedy "explicit locking" (DDIA ch08 p.300).
  - Photo events follow from what is really written, so a merged photo change still causes the right events (ADR-0008).
- **How the merge works** (`RecipeMerge`, pure, in `Koch.Recipes.Domain`). The recipe is a map of parts; for each part,
  the side that changed it gives it:
  - **Title, notes, photo, the steps:** if both sides changed the part, the phone's version (it arrives last) wins.
    The steps are one part: merging free text line by line would need a text CRDT or OT (DDIA p.227-228), which is out
    of proportion here.
  - **Ingredient lines:** a collection, merged line by line.
    - A line is known by its ingredient and how many lines of that ingredient come before it (butter for the dough,
      butter for the tin).
    - Removals are tracked against the base. An untouched copy on the other side doesn't bring a removed line back,
      which avoids the shopping-cart anomaly (DDIA p.225).
    - A line removed on one side and edited on the other keeps the edit.
    - Lines added on the service go after the line they followed there.
  - **Servings and amounts belong together.** Once either side changed the servings, lines are not mixed. The side
    that changed servings or lines gives both; when both did, the phone that arrives last.
  - **When the merge breaks a rule,** for example more than 100 lines together, the phone's recipe wins whole. Some
    invariants can't be merged (DDIA p.227).
- **Convergence:** the service merges in arrival order and answers with its book, so every phone ends up with the same
  recipe. The randomized three-phone test (200 histories) now also edits single parts.
- **Known limit:**
  1. A sync's answer got lost after the service had saved it.
  2. The phone then changed a part back to exactly what its base had.

  The merge sees no change from the phone in that part and keeps the lost sync's value. The phone shows it after the
  sync, and it can be changed back again.

  The alternative, sending no base after a failed sync, would bring back whole-recipe "last arrival wins" for every
  edit made offline, which is the case this ADR is for. A test documents the limit.
- **Protocol 3:**
  - Older apps get 409 and the "please reload" banner, so every phone sends bases.
  - The phone's local store keeps unanswered changes as a request, so it moves to schema 3. Schemas 1 and 2 still read.
  - New contract samples `v3-2026-11-22` for `local`, `sync-request` and `sync-response`.

### 2. Browser tests of the whole household (`tests/Koch.E2E`)
- **The setup:**
  - The real gateway runs on a real port (`WebApplicationFactory.UseKestrel`), serving the real Blazor app and
    checking sign-in as behind Easy Auth.
  - Its proxy reaches Recipes and Shopping in-process, on their memory stores.
  - Two browser contexts are Anna's and Ben's phones: 390×844, de-DE, each with its own local storage.
  - Locally the tests use the installed Edge. In CI they use Playwright's Chromium (`PLAYWRIGHT_CHANNEL` empty).
- **Scenarios:**
  - **Merge:** a recipe created on one phone reaches the other. One phone goes offline and adds pepper while the other
    renames the dish, and both phones end with both edits. With the bases switched off this test fails: checked once by
    hand.
  - **Shopping list:** a list from two recipes adds 500 g + 300 g potatoes to 800 g, and a tick on Ben's phone shows on
    Anna's.
  - **Accessibility:** axe finds no serious or critical WCAG 2.1 AA problem on the start page, the recipe form, the
    lists and the account page (the same rule set as fallstrick).
- **Not covered:** photos, which need Azurite, and sign-in itself, which is Easy Auth in Azure. The gateway's own tests
  cover the headers.

### 3. No Service Bus Standard topic
ADR-0007 made a topic conditional on a second consumer of an event. There still is none. Media is the only consumer of
both queues, so a topic would only add cost (about $10 a month for Standard) and moving parts. The trigger stays
written down in ADR-0007 and ADR-0008.

## Consequences
- **A concurrent edit is lost** only when both phones changed the same part, or when servings and lines clash. Before,
  it was lost whenever both phones changed anything in the recipe. The losing version is still in the temporal history.
- **Bigger requests:** each changed recipe travels twice, as itself and as its base, a few KB. Unchanged recipes don't
  travel.
- **Shopping lists don't merge:** their items are already independent per contribution (ADR-0005), so concurrent
  additions never conflicted.
- **CI:** about 1 minute more for installing Chromium and running the browser tests.

## Compliance
- `Koch.Recipes.Domain.Tests`:
  - `RecipeMergeTests`: different parts, the same part, lines added, removed and edited on both sides, two lines of
    one ingredient, servings, idempotence, a merge over the limit;
  - `BasesTests`;
  - `SyncTests`: two phones, an offline edit, the known limit, and 200 random histories with part edits;
  - contract samples v3.
- `Koch.Recipes.Tests`:
  - the row store merges with the stored row and never with a missing or removed one (SQL and memory);
  - the API merges with a base, replaces the recipe whole without one, and refuses a base for a recipe it wasn't sent.
- `Koch.E2E`: the scenarios above.

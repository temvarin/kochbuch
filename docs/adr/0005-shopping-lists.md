# ADR-0005: Shopping lists: built on the phone, one item per contribution, a shared kernel

**Status:** Accepted (2026-11-22)

## Context
The household chooses recipes and servings and gets one shopping list with matching ingredients combined: 500 g +
300 g Kartoffeln = 800 g. In the shop, both phones tick items, often with poor reception. ADR-0001 made Shopping its
own service, and the weakest boundary.

Three ways the Shopping service could learn a recipe's ingredients:
1. Call the Recipes service while building a list. That couples the two in time, and cold starts stack up.
2. Keep a copy of recipes, updated from events. That needs replay and catch-up logic, and building a list becomes
   online-only.
3. Have the phone send what goes on the list. The phone already holds every recipe offline.

The recipe and shopping domains also need the same vocabulary (units, quantities, shop aisles) and the same sync
machinery. Copying them would let two versions of one rule drift apart.

## Decision
- **The list is built on the phone (option 3).**
  - The app turns the chosen recipes into planned lines, scaled to the servings and with the catalogue's aisle.
  - `Koch.Shopping.Domain` puts them on the list.
  - The Shopping service checks and stores lists; it never sees a recipe.
  - A list is a snapshot: editing a recipe later does not change last week's list.
- **One item per contribution, combined when shown.**
  - Each recipe line becomes its own `ShoppingItem`, and so does each thing added by hand.
  - Contributions of the same ingredient (case and spacing ignored) are shown as one `ShoppingLine`.
  - When both phones add recipes to the same list offline, their contributions simply add up, and neither overwrites
    the other. Merging independent additions is DDIA ch06 p.227; a single combined item would have lost one phone's
    amount under last-arrival-wins.
  - A line shows what is still **missing**, so a recipe added after the potatoes were bought brings the line back with
    only the extra amount.
- **Combining amounts** (`Amounts.Combine`, which ignores order):
  - Mass adds with mass and volume with volume (TL = 5 ml, EL = 15 ml), shown in the largest sensible unit.
  - Amounts that are all in one unit stay in it.
  - Counted units add only with the same unit and are rounded up.
  - Mass and volume never mix: there is no density table.
- **Shared kernel `Koch.Kernel`:**
  - Contents: `IItem`, `ItemChanges`, the generic `Synced<TBook, TChanges>` over the `ISyncBook` / `IBookChanges`
    abstractions, `Unit` / `Quantity`, `Aisle`, `Text`, `IKeyValueStore`.
  - Rationale (Clean Code ch20):
    - It is depended on by both domains, so it must be stable (SDP p.422).
    - Its sync part is abstractions the domains implement (SAP p.428).
    - The rest is vocabulary that changes for one reason only, the household's units and shop (CCP p.412).
  - No domain rules: `Koch.Shopping.Domain` knows nothing of recipes, and the architecture tests check that.
- **Shared row store.** The SQL upsert, tombstones and per-request transaction moved into
  `Koch.ServiceDefaults.Documents`, with the schema name as a parameter, so both services store rows identically.
  - The sync endpoints stay one per service and are deliberately alike: shared plumbing, but not shared protocol code.
    Newman warns that sharing code across services couples their releases.

## Consequences
- Lists can be built offline. Shopping is a thin service: it validates, stores and syncs. If it never grows behaviour
  of its own, merging it into Recipes is a module move (ADR-0001).
- The aisle of a line follows the catalogue as it is now, so changing an aisle reorders existing lists.
- Removed ticked items leave tombstone rows; a weekly shop adds a few dozen. Purging old tombstones can come later.
- A kernel change rebuilds both services. Accepted, because the kernel holds only what rarely changes.

## Compliance
- `Koch.Shopping.Domain.Tests`:
  - the combining table, including the brief's 500 g + 300 g;
  - order independence over 500 random sets, each shuffled 5 times;
  - two phones adding offline sum up;
  - a tick plus a later addition brings the line back as "missing";
  - convergence over 200 random shop visits;
  - contract samples.
- `Koch.Shopping.Tests`: the sync API, the real `db/shopping` scripts, and row samples.
- `Koch.App.Tests`: choosing recipes gives "800 g … für Kartoffelgratin, Kartoffelsuppe"; servings scale; ticking;
  adding by hand; aisles; the list reaching the service.
- `Koch.Architecture.Tests`: an allow-list of what each project may reference.

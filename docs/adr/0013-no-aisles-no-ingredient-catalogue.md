# ADR-0013: No shop aisles, and so no ingredient catalogue

**Status:** Accepted (2026-11-22). Supersedes the aisle parts of ADR-0005 and the ingredient catalogue of ADR-0004.

## Context
Shopping lists were grouped by shop aisle (fruit and vegetables, chilled, …). To make that work:
- every ingredient had an entry in a catalogue that both phones synced, holding its aisle;
- a page, "Zutaten und Gänge", set the aisles;
- each shopping item carried the aisle it was added with, and the list followed the catalogue's current one.

In use, the household didn't maintain the aisles. New ingredients landed in "Sonstiges", so the grouping mostly showed
one group, and the page was one more thing to understand. The owner asked for the "aisle finder" to go.

The catalogue had exactly one other use: suggesting names while typing an ingredient. The recipes already name every
ingredient, so the suggestions can come from them.

## Decision
- **No aisles anywhere:**
  - the `Aisle` type is gone from the shared kernel;
  - the aisle is gone from shopping items, their rows and the sync format;
  - the "Zutaten und Gänge" page is gone.
- **A list shows what is still to buy first, by name, then what is in the trolley.**
- **No ingredient catalogue:**
  - the recipe book is just its recipes;
  - name suggestions are every ingredient the recipes name, once (`RecipeBook.IngredientNames`).
- **`IngredientKey` stays.** The shopping list adds up lines with the same ingredient, and the recipe merge matches
  lines by it.
- **Formats:**
  - **Recipes:** sync protocol 4 has no `ingredients` section; the phone's store moves to schema 4.
  - **Shopping:** sync protocol 2 has items without `aisle`; the phone's store moves to schema 2.
  - Older apps get 409 and the "please reload" banner.
  - Older local data and older samples still read: the removed fields are ignored.
- **Rows:**
  - `ingredient:{id}` rows stay in `recipes.Documents`, unread. Removing them would be a data migration for nothing.
  - Shopping item rows from before still read, and their `aisle` is ignored.

## Consequences
- Less to sync, store and explain. Gone: one page, one synced item type, one kernel type, one service.
- A list in a big shop is no longer in walking order. If that is missed, a cheap alternative is to sort by the order
  in which items were ticked off last time, which needs no setup. It isn't built.
- **One-way change:** a service from before this ADR can't read item rows written after it, because it requires
  `aisle`. Rolling back across it means restoring the old rows from the temporal history. Nothing had been deployed
  yet when this was decided.

## Compliance
- Contract samples:
  - Recipes: `local`, `sync-request` and `sync-response` v4;
  - Shopping: `local`, `sync-request` and `sync-response` v2, and `row-item` v2.
  - The older samples stay readable. The Shopping contract test now requires an exact round trip only of the current
    version, as the Recipes one already did.
- `RowContractTests` (Recipes): old ingredient rows are ignored.
- `CatalogTests`: suggestions come from the recipes, once each.
- `ShoppingBookTests`: open lines first by name, ticked ones last.

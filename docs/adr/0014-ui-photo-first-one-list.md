# ADR-0014: The app's UI: photo first, one shopping list, cooking view, ingredients typed as one line

**Status:** Accepted (2026-11-23). Supersedes the dated lists of ADR-0005.

## Context
The first UI worked, but the owner found it lifeless and unintuitive:
- **Recipes were lines of text.** The photo, the most appetising part, was barely visible.
- **Every action was a full-width bordered button of the same weight.** "Bearbeiten", "Erledigtes entfernen" and
  "Liste löschen" looked as important as the main action.
- **Shopping took a detour.** You created a list named after the date, then picked recipes on another page. People
  think "put this recipe on the shopping list", not "make a list".
- **Nothing helped while cooking,** and typing an ingredient meant four fields: amount, unit, ingredient, note.

Three directions were mocked up in the app's own colours and fonts (the "Kochbuch UI-Entwürfe" canvas). The owner
chose **A · Foto zuerst**, which includes the five changes below.

## Decision
1. **Photo first.**
   - Recipes are tiles in two columns.
   - A recipe without a photo, or whose photo isn't ready yet, gets a tile in gold, ink or white with its initial. The
     colour comes from its id, so it is the same on both phones.
   - The recipe page opens on the picture, with back and ⋯ buttons on it.
2. **One main action per screen; the rare ones behind ⋯.**
   - The recipe page has "Auf die Liste" and "Kochen" at the bottom. "Bearbeiten" and "Löschen" (with confirmation) are
     in its ⋯ menu.
   - The list has "Erledigte leeren" and "Liste leeren" (with confirmation) in its ⋯ menu.
   - The menu is the browser's `<details>` element: keyboard and screen readers know it, and it needs no script.
3. **One shopping list** (data change, below).
   - A recipe goes onto it from its page, at the servings shown there. Once added, the button says "Auf der Liste", so a
     second tap doesn't add everything twice.
   - The start page shows "Einkaufsliste · n offen" and a "+" for a new recipe, always within reach.
4. **Cooking view** (`/rezept/{id}/kochen`):
   - one step per screen, large, with progress;
   - the ingredients the step names, scaled to the servings chosen;
   - the screen kept on through the Screen Wake Lock API, taken again when the app comes back to the front. Where the
     browser doesn't offer it, the screen sleeps as usual.
5. **Ingredients typed as one line:** "500 g Kartoffeln, festkochend", "2 Zehen Knoblauch", "1 Zwiebel", "Salz".
   - `IngredientText` reads amount (including "1,5", "1/2", "1 ½"), unit (codes plus common words such as "Zehen",
     "Liter", "Stk") and ingredient, and after the first comma a note.
   - While you type, the field shows how the line will be read. What can't be read is explained.
   - A line tapped in the list goes back into the field to change it and keeps its place.
   - A line still in the field when saving is saved with the recipe.
6. **The sync state is on the start page:** "Abgeglichen · vor 2 Min.", or the existing banner when something is
   wrong. Before, it was only on the account page.
7. **The same visual language as before:** cream, ink and gold, Onest and Unbounded, thick outlines and offset shadows.
   Inline SVG icons replace the emoji tab icons. The stylesheet lost the rules for 35 classes that kassensturz left
   behind (budgets, statistics, months) and nothing used.

### Data: one list
- **Shopping sync protocol 3** has items only: no lists, no `listId` on items. The phone's store moves to schema 3.
  Older stores and samples still read: their lists are ignored, and every item lands on the one list.
- **Rows:** `list:{id}` rows stay in `shopping.Documents`, unread. Item rows from before still read, and their
  `listId` is ignored.
- **The domain:**
  - `ShoppingBook` holds the items, and `ShoppingList` is gone.
  - `Add`, `Check` and `RemoveChecked` no longer take a list.
  - `OpenRecipes` names the recipes something is still to be bought for.
- **What is lost:**
  - Several lists at once, e.g. one per shop. The household didn't use them.
  - Items of a list that was deleted on one phone while the other added to it used to be kept but hidden. They now show
    on the list. Nothing had been deployed when this changed.

## Consequences
- The app has fewer pages: the dated lists overview, the recipe picker and the aisles page are gone. The cooking view
  is new.
- Typing an ingredient is one field instead of four. The cost: the parser has to understand how German recipes write
  amounts. Its tests list what it reads and what it refuses.
- **One-way change:** a Shopping service from before this ADR can't read item rows written after it, because it
  requires `listId`. Rolling back means restoring rows from the temporal history, as for ADR-0013.
- **Not built:** a timer suggested by a step ("5 Minuten") from the mockup, and removing one recipe's items from the
  list again.

## Compliance
- `Koch.App.Tests`:
  - `IngredientTextTests`: the lines it reads, round trips, the lines it refuses;
  - `RecipeTests`: typing lines, the line left in the field, an edit that keeps exact amounts and order, deleting
    from the menu, the cooking view with scaled ingredients and the wake lock;
  - `ShoppingTests`: adding from the recipe page, servings, no double add, ticking, emptying with confirmation, the
    list reaching the service.
- `Koch.Shopping.Domain.Tests`: the one list, the open recipes, contract samples v3. `Koch.Shopping.Tests`: rows v3,
  and old rows still read.
- `Koch.E2E`: two phones, an offline edit merged, two recipes on the list from their pages adding up, a tick seen
  on the other phone, and axe on the start page, the recipe form, the list, the account page, a recipe page and the
  cooking view.

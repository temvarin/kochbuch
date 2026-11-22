using Koch.Kernel;
using Koch.Recipes.Domain.Cooking;

namespace Koch.Recipes.Domain.Sync;

/// <summary>The recipe book on a phone and what the phone knows about its sync with the Recipes service (docs/adr/0004).</summary>
public sealed class SyncedBook : Synced<RecipeBook, BookChanges>
{
    public SyncedBook()
    {
    }

    public SyncedBook(RecipeBook book, SyncState<RecipeBook>? state, BookChanges? unconfirmed) : base(book, state, unconfirmed)
    {
    }

    /// <summary>
    /// What the service needs to merge the changed recipes with edits from the other phone: each one as this phone last
    /// got it from the service (docs/adr/0012); none for a new recipe.
    /// <para>
    /// Also after syncs that got no answer, which is what editing offline looks like. If such a sync did arrive, the
    /// service already has this phone's earlier edit; the merge keeps it unless the phone changed the same part again.
    /// Only one case comes out wrong: a part changed in the lost sync and then changed back to exactly what the base
    /// had. The service keeps the earlier edit, and the phone shows it after the sync, to be changed back again.
    /// </para>
    /// </summary>
    public IReadOnlyList<Recipe> BasesFor(BookChanges changes) => State is null
        ? []
        : [.. changes.Recipes.Changed.Select(r => State.Base.Find(r.Id)).OfType<Recipe>()];
}

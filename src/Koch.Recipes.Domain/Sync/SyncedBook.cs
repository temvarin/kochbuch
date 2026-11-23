using Koch.Kernel;

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
}

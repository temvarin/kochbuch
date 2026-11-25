using Koch.Domain.Kernel;
using Koch.Domain.Recipes;
using Koch.Domain.Shopping;

namespace Koch.App.Shell;

/// <summary>
/// One synced book while the app runs: the recipes or the shopping lists. Pages don't change it directly; they go
/// through the service of their area.
/// </summary>
public abstract class DataState<TBook, TChanges>(Synced<TBook, TChanges> data, Action<Synced<TBook, TChanges>> save)
    where TBook : class, ISyncBook<TBook, TChanges>
    where TChanges : class, IBookChanges<TBook, TChanges>
{
    public Synced<TBook, TChanges> Data { get; } = data;

    /// <summary>After every change, including those a sync brings.</summary>
    public event Action? Changed;

    /// <summary>Changes, saves and announces in one step. If the domain refuses the change, nothing is saved.</summary>
    public void Change(Action<Synced<TBook, TChanges>> change)
    {
        change(Data);
        save(Data);
        Changed?.Invoke();
    }
}

/// <summary>The recipe book (localStorage key koch.recipes).</summary>
public sealed class RecipeState(BookStore store) : DataState<RecipeBook, BookChanges>(store.Load(), store.Save);

/// <summary>The shopping lists (localStorage key koch.shopping).</summary>
public sealed class ShoppingState(ShoppingStore store)
    : DataState<ShoppingBook, ShoppingChanges>(store.Load(), store.Save);

using Koch.App.Account;
using Koch.App.Sync;
using Microsoft.AspNetCore.Components;

namespace Koch.App.Shell;

/// <summary>
/// A page that shows synced data or the sync state: it redraws when either changes, e.g. when a sync brings the other
/// phone's recipes or ticks.
/// </summary>
public abstract class LiveComponent : ComponentBase, IDisposable
{
    [Inject] private RecipeState Recipes { get; set; } = default!;
    [Inject] private ShoppingState Shopping { get; set; } = default!;
    [Inject] private SyncOverview Sync { get; set; } = default!;
    [Inject] private AccountService Account { get; set; } = default!;

    protected override void OnInitialized()
    {
        Recipes.Changed += Redraw;
        Shopping.Changed += Redraw;
        Sync.Changed += Redraw;
        Account.Changed += Redraw;
    }

    public void Dispose()
    {
        Recipes.Changed -= Redraw;
        Shopping.Changed -= Redraw;
        Sync.Changed -= Redraw;
        Account.Changed -= Redraw;
        GC.SuppressFinalize(this);
    }

    private void Redraw() => _ = InvokeAsync(StateHasChanged);
}

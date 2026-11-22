using Koch.App.Account;
using Koch.App.Recipes;
using Koch.App.Shell;
using Koch.App.Shopping;
using Koch.App.Sync;
using Koch.Recipes.Domain.Storage;
using Koch.Shopping.Domain;

namespace Koch.App;

public static class ServiceRegistration
{
    /// <summary>
    /// Everything the app needs except the browser's storage, the clock and the network, which the caller provides:
    /// Program.cs the real ones, the tests their own.
    /// </summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services) => services
        .AddSingleton<BookStore>()
        .AddSingleton<ShoppingStore>()
        .AddSingleton<RecipeState>()
        .AddSingleton<ShoppingState>()
        .AddSingleton<AccountService>()
        .AddSingleton<RecipeSync>()
        .AddSingleton<ShoppingSync>()
        .AddSingleton<SyncOverview>()
        .AddSingleton<RecipeService>()
        .AddSingleton<PhotoService>()
        .AddSingleton<ShoppingService>()
        .AddSingleton<Browser>();
}

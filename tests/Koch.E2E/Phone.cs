using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Koch.E2E;

/// <summary>One member's phone: a browser context with the app open, and the steps a person takes in it.</summary>
public sealed class Phone(IBrowserContext context, IPage page) : IAsyncDisposable
{
    public IPage Page => page;

    /// <summary>Loads the app at <paramref name="path"/> and waits until Blazor shows the page's heading.</summary>
    public async Task OpenAsync(string path = "")
    {
        await page.GotoAsync(path);
        await page.Locator("h1").First.WaitForAsync();
    }

    /// <summary>Taken out of the network, as in the shop's cellar; the app stays open.</summary>
    public Task OfflineAsync(bool offline = true) => context.SetOfflineAsync(offline);

    /// <summary>Goes to the account page inside the app and syncs both books now, as a person would with the button.</summary>
    public async Task SyncAsync()
    {
        await GoToAsync("Rezepte");
        await page.GetByLabel("Konto und Abgleich").ClickAsync();
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Jetzt abgleichen" });
        await Expect(button).ToBeEnabledAsync();
        var recipes = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/recipes/sync", StringComparison.Ordinal) && r.Ok);
        var shopping = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/shopping/sync", StringComparison.Ordinal) && r.Ok);
        await button.ClickAsync();
        await Task.WhenAll(recipes, shopping);
        await Expect(page.GetByTestId("sync-status")).ToHaveTextAsync("Abgeglichen · gerade eben");
    }

    /// <summary>A tab of the bottom bar: "Rezepte" or "Einkauf". In-app navigation, so it works offline too.</summary>
    public Task GoToAsync(string tab) =>
        page.GetByRole(AriaRole.Navigation, new() { Name = "Bereiche" }).GetByRole(AriaRole.Link, new() { Name = tab }).ClickAsync();

    public async Task OpenRecipeAsync(string title)
    {
        await GoToAsync("Rezepte");
        await page.Locator("a.tile", new() { HasText = title }).ClickAsync();
        await Expect(page.Locator("h1.recipe-title")).ToHaveTextAsync(title);
    }

    /// <summary>From the recipe page: the ⋯ menu, then "Bearbeiten".</summary>
    public async Task EditRecipeAsync()
    {
        await page.GetByLabel("Mehr: Bearbeiten, Löschen").ClickAsync();
        await page.GetByTestId("edit").ClickAsync();
    }

    /// <summary>Fills a field the way typing does, then leaves it, so Blazor's change binding sees the value.</summary>
    public static async Task SetAsync(ILocator field, string value)
    {
        await field.FillAsync(value);
        await field.PressAsync("Tab");
    }

    /// <summary>Types an ingredient as one line, "500 g Kartoffeln", and presses Enter, as on a phone keyboard.</summary>
    public async Task AddLineAsync(string line)
    {
        await page.GetByTestId("new-line").FillAsync(line);
        await page.GetByTestId("new-line").PressAsync("Enter");
        await Expect(page.GetByTestId("new-line")).ToHaveValueAsync("");
    }

    public async Task SaveRecipeAsync(string title)
    {
        await page.GetByTestId("save").ClickAsync();
        await Expect(page.Locator("h1.recipe-title")).ToHaveTextAsync(title);
    }

    /// <summary>The ingredient names as the recipe page shows them.</summary>
    public Task<IReadOnlyList<string>> IngredientsAsync() => page.Locator("li.ingredient").AllInnerTextsAsync();

    public async ValueTask DisposeAsync() => await context.DisposeAsync();
}

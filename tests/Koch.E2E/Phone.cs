using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Koch.E2E;

/// <summary>One member's phone: a browser context with the app open, and the steps a person takes in it.</summary>
public sealed class Phone(IBrowserContext context, IPage page) : IAsyncDisposable
{
    public IPage Page => page;

    /// <summary>Loads the app at <paramref name="path"/> and waits until Blazor shows the page's title.</summary>
    public async Task OpenAsync(string path = "")
    {
        await page.GotoAsync(path);
        await page.Locator(".topbar h1").WaitForAsync();
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
        await Expect(page.GetByTestId("sync-status")).ToHaveTextAsync("zuletzt gerade eben");
    }

    /// <summary>A tab of the bottom bar: "Rezepte" or "Einkauf". In-app navigation, so it works offline too.</summary>
    public Task GoToAsync(string tab) =>
        page.GetByRole(AriaRole.Navigation, new() { Name = "Bereiche" }).GetByRole(AriaRole.Link, new() { Name = tab }).ClickAsync();

    public async Task OpenRecipeAsync(string title)
    {
        await GoToAsync("Rezepte");
        await page.Locator("a.line", new() { HasText = title }).ClickAsync();
        await Expect(page.Locator(".topbar h1")).ToHaveTextAsync(title);
    }

    /// <summary>Fills a field the way typing does, then leaves it, so Blazor's change binding sees the value.</summary>
    public static async Task SetAsync(ILocator field, string value)
    {
        await field.FillAsync(value);
        await field.PressAsync("Tab");
    }

    /// <summary>Adds a line at the form's empty last line, which the form keeps for the next ingredient.</summary>
    public async Task AddLineAsync(string ingredient, string? amount = null, string unit = "g")
    {
        var line = page.GetByTestId("line").Last;
        if (amount is not null)
        {
            await SetAsync(line.GetByTestId("amount"), amount);
            await line.GetByTestId("unit").SelectOptionAsync(new SelectOptionValue { Label = unit });
        }
        await SetAsync(line.GetByTestId("ingredient"), ingredient);
    }

    public async Task SaveRecipeAsync(string title)
    {
        await page.GetByTestId("save").ClickAsync();
        await Expect(page.Locator(".topbar h1")).ToHaveTextAsync(title);
    }

    /// <summary>The ingredient names as the recipe page shows them.</summary>
    public Task<IReadOnlyList<string>> IngredientsAsync() => page.Locator("li.ingredient").AllInnerTextsAsync();

    public async ValueTask DisposeAsync() => await context.DisposeAsync();
}

using Koch.App.Account;
using Koch.App.Recipes;
using Koch.App.Sync;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Cooking;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.App.Tests;

/// <summary>The recipe pages on a phone, syncing with the real Recipes service.</summary>
public sealed class RecipeTests : AppTestContext
{
    private static readonly Guid SoupId = new("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80");

    private static Recipe Soup(string title = "Kartoffelsuppe") => new(SoupId, title, 4,
        [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm), "festkochend"), new RecipeLine("Salz", null)],
        ["Schälen.", "Kochen."]);

    [Fact]
    public async Task A_recipe_typed_in_on_the_phone_reaches_the_service()
    {
        await Sync.StartAsync();
        var page = Render<RecipeEdit>();
        page.Find("[data-testid=title]").Change("Kartoffelsuppe");
        page.Find("[data-testid=servings]").Change("4");
        page.Find("[data-testid=amount]").Change("1,5");
        page.Find("[data-testid=unit]").Change(nameof(Unit.Kilogramm));
        page.Find("[data-testid=ingredient]").Change("Kartoffeln");
        page.Find("[data-testid=step]").Change("Schälen und kochen.");

        page.Find("form").Submit();
        AfterSave();

        await WaitFor(() => !Sync.HasUnsyncedChanges && Sync.Status == SyncStatus.Ready);
        var recipe = Assert.Single((await Server.BookAsync()).Recipes);
        Assert.Equal("Kartoffelsuppe", recipe.Title);
        Assert.Equal(new RecipeLine("Kartoffeln", new Quantity(1.5m, Unit.Kilogramm)), Assert.Single(recipe.Lines));
        Assert.Equal(["Schälen und kochen."], recipe.Steps);
    }

    [Fact]
    public async Task The_other_phones_recipe_shows_up_in_the_list()
    {
        await Server.OnOtherPhone(book => book.Save(Soup()));

        var list = Render<RecipeList>();
        await Sync.StartAsync();

        list.WaitForAssertion(() => Assert.Contains("Kartoffelsuppe", list.Find(".list").TextContent));
    }

    [Fact]
    public async Task A_lost_answer_neither_loses_nor_doubles_the_recipe()
    {
        await Sync.StartAsync();
        Server.LoseNextAnswer = true;

        Recipes.Save(Soup());
        AfterSave();
        await WaitFor(() => Sync.Status == SyncStatus.Failed);
        Clock.Advance(SyncTiming.RetryAfter); // the one retry

        await WaitFor(() => Sync.Status == SyncStatus.Ready && !Sync.HasUnsyncedChanges);
        Assert.Equal(Soup(), Assert.Single((await Server.BookAsync()).Recipes));
        Assert.Equal(1, Server.RecipeRows.VersionOf($"recipe:{SoupId}"));
    }

    [Fact]
    public async Task Offline_a_recipe_stays_on_the_phone_and_goes_out_once_the_network_is_back()
    {
        Server.Offline = true;
        await Sync.StartAsync();
        Recipes.Save(Soup());
        AfterSave();

        var list = Render<RecipeList>();
        Assert.Contains("Kartoffelsuppe", list.Markup);
        Assert.Equal(AccountStatus.Unreachable, Services.GetRequiredService<AccountService>().Status);
        Assert.Empty((await Server.BookAsync()).Recipes);

        Server.Offline = false;
        await Sync.ResumeAsync();

        await WaitFor(() => !Sync.HasUnsyncedChanges);
        Assert.Single((await Server.BookAsync()).Recipes);
    }

    [Fact]
    public async Task The_recipes_survive_closing_the_app()
    {
        Server.Offline = true;
        Recipes.Save(Soup());

        // A new start reads what the last one saved.
        var restarted = new Koch.Recipes.Domain.Storage.BookStore(Storage).Load();

        Assert.Equal(Soup(), Assert.Single(restarted.Book.Recipes));
        await Task.CompletedTask;
    }

    [Fact]
    public void The_cook_view_scales_the_amounts_to_the_servings_chosen()
    {
        Recipes.Save(Soup());
        var page = Render<RecipeView>(parameters => parameters.Add(p => p.Id, SoupId));
        Assert.Contains("500 g", page.Find("[data-testid=ingredients]").TextContent);

        for (var i = 0; i < 4; i++) page.Find("[aria-label='Mehr Portionen']").Click();

        Assert.Equal("8", page.Find("[data-testid=servings]").TextContent);
        var ingredients = page.Find("[data-testid=ingredients]").TextContent;
        Assert.Contains("1 kg", ingredients);
        Assert.Contains("Kartoffeln, festkochend", ingredients);
        Assert.Equal(4, Recipes.Find(SoupId)!.Servings); // only the view changed
    }

    [Fact]
    public void An_amount_the_app_cant_read_is_explained_and_nothing_is_saved()
    {
        var page = Render<RecipeEdit>();
        page.Find("[data-testid=title]").Change("Suppe");
        page.Find("[data-testid=amount]").Change("viel");
        page.Find("[data-testid=ingredient]").Change("Salz");

        page.Find("form").Submit();

        Assert.Contains("„viel“ verstehe ich nicht", page.Find("[data-testid=error]").TextContent);
        Assert.Empty(Recipes.All);
    }

    [Fact]
    public void Editing_shows_the_exact_amounts_and_saving_unchanged_changes_nothing()
    {
        var recipe = new Recipe(SoupId, "Suppe", 3, [new RecipeLine("Mehl", new Quantity(12.345m, Unit.Gramm))], []);
        Recipes.Save(recipe);
        var page = Render<RecipeEdit>(parameters => parameters.Add(p => p.Id, SoupId));
        Assert.Equal("12,345", page.Find("[data-testid=amount]").GetAttribute("value"));

        page.Find("form").Submit();

        Assert.Equal(recipe, Recipes.Find(SoupId));
    }

    [Fact]
    public void A_removed_recipe_is_gone_from_the_list()
    {
        Recipes.Save(Soup());
        var page = Render<RecipeEdit>(parameters => parameters.Add(p => p.Id, SoupId));

        page.Find("[data-testid=remove]").Click();
        page.Find("[data-testid=remove-confirm]").Click();

        Assert.Empty(Recipes.All);
    }

    [Fact]
    public async Task Signed_out_the_list_offers_the_sign_in()
    {
        Server.User = null;
        var list = Render<RecipeList>();

        await Sync.StartAsync();

        list.WaitForAssertion(() => Assert.NotNull(list.Find("[data-testid=sign-in]")));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}

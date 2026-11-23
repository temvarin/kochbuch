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
        page.Find("[data-testid=new-line]").Input("1,5 kg Kartoffeln");
        page.Find("[data-testid=step]").Change("Schälen und kochen.");

        page.Find("[data-testid=save]").Click(); // the line still in the field is saved with it
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

        list.WaitForAssertion(() => Assert.Contains("Kartoffelsuppe", list.Find(".tiles").TextContent));
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

        Assert.Equal("8 Portionen", page.Find("[data-testid=servings]").TextContent);
        var ingredients = page.Find("[data-testid=ingredients]").TextContent;
        Assert.Contains("1 kg", ingredients);
        Assert.Contains("Kartoffeln, festkochend", ingredients);
        Assert.Equal(4, Recipes.Find(SoupId)!.Servings); // only the view changed
    }

    [Fact]
    public void Lines_typed_one_after_another_are_read_and_shown_as_they_are_read()
    {
        var page = Render<RecipeEdit>();

        foreach (var line in new[] { "500 g Kartoffeln, festkochend", "2 Zehen Knoblauch", "Salz" })
        {
            page.Find("[data-testid=new-line]").Input(line);
            page.Find("form.add-line").Submit();
        }

        Assert.Equal(["500 g Kartoffeln, festkochend", "2 Zehe Knoblauch", "– Salz"],
            page.FindAll("[data-testid=lines] .line").Select(l => string.Join(' ', l.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        Assert.Equal("", page.Find("[data-testid=new-line]").GetAttribute("value"));
    }

    [Fact]
    public void An_amount_the_app_cant_read_is_explained_and_nothing_is_saved()
    {
        var page = Render<RecipeEdit>();
        page.Find("[data-testid=title]").Change("Suppe");
        page.Find("[data-testid=new-line]").Input("1,2,3 g Salz");

        page.Find("[data-testid=save]").Click();

        Assert.Contains("Menge in „1,2,3 g Salz“ verstehe ich nicht", page.Find("[data-testid=error]").TextContent);
        Assert.Empty(Recipes.All);
    }

    [Fact]
    public void A_line_taken_back_to_change_it_keeps_its_exact_amount_and_its_place()
    {
        var recipe = new Recipe(SoupId, "Suppe", 3,
            [new RecipeLine("Mehl", new Quantity(12.345m, Unit.Gramm)), new RecipeLine("Salz", null)], []);
        Recipes.Save(recipe);
        var page = Render<RecipeEdit>(parameters => parameters.Add(p => p.Id, SoupId));

        page.FindAll("[data-testid=lines] .line")[0].Click();
        Assert.Equal("12,345 g Mehl", page.Find("[data-testid=new-line]").GetAttribute("value"));
        page.Find("[data-testid=save]").Click();

        Assert.Equal(recipe, Recipes.Find(SoupId)); // unchanged: same amount, same order
    }

    [Fact]
    public void A_recipe_removed_from_its_menu_is_gone_after_a_confirmation()
    {
        Recipes.Save(Soup());
        var page = Render<RecipeView>(parameters => parameters.Add(p => p.Id, SoupId));

        page.Find("[data-testid=remove]").Click();
        Assert.Single(Recipes.All);
        page.Find("[data-testid=remove-confirm]").Click();

        Assert.Empty(Recipes.All);
    }

    [Fact]
    public void Cooking_shows_one_step_at_a_time_with_what_it_needs_for_the_servings_chosen()
    {
        Recipes.Save(new Recipe(SoupId, "Suppe", 4,
            [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm)), new RecipeLine("Salz", null)],
            ["Kartoffeln schälen.", "Mit Salz kochen."]));
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo($"rezept/{SoupId}/kochen?portionen=8"); // as the recipe page links to it
        var page = Render<CookMode>(parameters => parameters.Add(p => p.Id, SoupId));

        Assert.Equal("Kartoffeln schälen.", page.Find("[data-testid=step-text]").TextContent);
        Assert.Equal("1 kg Kartoffeln", page.Find("[data-testid=needed]").TextContent.Trim());

        page.Find("[data-testid=next]").Click();

        Assert.Equal("Mit Salz kochen.", page.Find("[data-testid=step-text]").TextContent);
        Assert.Equal("Salz", page.Find("[data-testid=needed]").TextContent.Trim());
        Assert.NotNull(page.Find("[data-testid=done]"));
        JSInterop.VerifyInvoke("koch.keepScreenOn");
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

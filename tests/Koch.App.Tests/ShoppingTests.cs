using Koch.App.Recipes;
using Koch.App.Shopping;
using Koch.App.Sync;
using Koch.Domain.Measures;
using Koch.Domain.Recipes;

namespace Koch.App.Tests;

/// <summary>
/// The one shopping list on a phone: recipes go onto it from their page, syncing with the real Shopping service
/// (docs/adr/0014).
/// </summary>
public sealed class ShoppingTests : AppTestContext
{
    private static readonly Recipe Soup = new(new Guid("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"), "Kartoffelsuppe", 4,
        [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm)), new RecipeLine("Zwiebeln", new Quantity(1, Unit.Stueck)),
         new RecipeLine("Salz", null)], []);

    private static readonly Recipe Gratin = new(new Guid("7a2e2d3b-8c5f-4d66-ae3c-2f1c6b7d8e91"), "Kartoffelgratin", 2,
        [new RecipeLine("kartoffeln", new Quantity(300, Unit.Gramm)), new RecipeLine("Sahne", new Quantity(200, Unit.Milliliter))], []);

    /// <summary>"Auf die Liste" on the recipe's page, at the servings shown there.</summary>
    private void PutOnList(Recipe recipe, int moreServings = 0)
    {
        Recipes.Save(recipe);
        var page = Render<RecipeView>(p => p.Add(x => x.Id, recipe.Id));
        for (var i = 0; i < moreServings; i++) page.Find("[aria-label='Mehr Portionen']").Click();
        page.Find("[data-testid=add-to-list]").Click();
    }

    [Fact]
    public void Two_recipes_put_on_the_list_make_one_line_of_the_same_ingredient_with_the_sum()
    {
        PutOnList(Soup);
        PutOnList(Gratin);

        var list = Render<ShoppingPage>();

        var potatoes = list.FindAll("[data-testid=line]").Single(l => l.TextContent.Contains("Kartoffeln"));
        Assert.Contains("800 g", potatoes.TextContent);
        Assert.Contains("für Kartoffelgratin, Kartoffelsuppe", potatoes.TextContent);
        Assert.Equal("Für Kartoffelgratin, Kartoffelsuppe", list.Find("[data-testid=for]").TextContent);
    }

    [Fact]
    public void The_servings_shown_on_the_recipe_scale_what_goes_on_the_list()
    {
        PutOnList(Soup, moreServings: 4); // 4 → 8 servings

        var line = Assert.Single(Shopping.Lines, l => l.Key == "kartoffeln");
        Assert.Equal([new Quantity(1, Unit.Kilogramm)], line.Missing);
    }

    [Fact]
    public void A_recipe_put_on_the_list_says_so_and_is_not_added_twice_by_a_second_tap()
    {
        Recipes.Save(Soup);
        var page = Render<RecipeView>(p => p.Add(x => x.Id, Soup.Id));

        page.Find("[data-testid=add-to-list]").Click();

        Assert.Empty(page.FindAll("[data-testid=add-to-list]"));
        Assert.Contains("Auf der Liste", page.Find("[data-testid=on-list]").TextContent);
        Assert.Equal([new Quantity(500, Unit.Gramm)], Assert.Single(Shopping.Lines, l => l.Key == "kartoffeln").Missing);
    }

    [Fact]
    public void Ticking_moves_a_line_into_the_trolley_and_emptying_the_trolley_clears_them()
    {
        PutOnList(Soup);
        var list = Render<ShoppingPage>();

        list.FindAll("[data-testid=line]").Single(l => l.TextContent.Contains("Salz")).Click();

        Assert.Contains("Salz", list.Find("[data-testid=checked]").TextContent);
        list.Find("[data-testid=remove-checked]").Click();
        Assert.DoesNotContain(Shopping.Lines, l => l.Key == "salz");
    }

    [Fact]
    public void Something_added_by_hand_joins_the_list()
    {
        var list = Render<ShoppingPage>();

        list.Find("[data-testid=by-hand]").Change("Milch");
        list.Find("form").Submit();

        Assert.Equal("Milch", Assert.Single(Shopping.Lines).Name);
    }

    [Fact]
    public void Emptying_the_list_asks_first()
    {
        PutOnList(Soup);
        var list = Render<ShoppingPage>();

        list.Find("[data-testid=clear]").Click();
        Assert.NotEmpty(Shopping.Lines);
        list.Find("[data-testid=clear-confirm]").Click();

        Assert.Empty(Shopping.Lines);
    }

    [Fact]
    public async Task The_list_reaches_the_shopping_service_and_survives_a_new_phone()
    {
        await Sync.StartAsync();
        PutOnList(Soup);
        PutOnList(Gratin);
        AfterSave();

        await WaitFor(() => !Sync.HasUnsyncedChanges && Sync.Status == SyncStatus.Ready);
        var list = await Server.ListsAsync();
        Assert.Equal([new Quantity(800, Unit.Gramm)], Assert.Single(list.Lines(), l => l.Key == "kartoffeln").Missing);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}

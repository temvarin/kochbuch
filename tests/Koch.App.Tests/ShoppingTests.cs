using Koch.App.Recipes;
using Koch.App.Shopping;
using Koch.App.Sync;
using Koch.Kernel;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Cooking;

namespace Koch.App.Tests;

/// <summary>The shopping pages on a phone, with recipes from the recipe book, syncing with the real Shopping service.</summary>
public sealed class ShoppingTests : AppTestContext
{
    private static readonly Recipe Soup = new(new Guid("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"), "Kartoffelsuppe", 4,
        [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm)), new RecipeLine("Zwiebeln", new Quantity(1, Unit.Stueck)),
         new RecipeLine("Salz", null)], []);

    private static readonly Recipe Gratin = new(new Guid("7a2e2d3b-8c5f-4d66-ae3c-2f1c6b7d8e91"), "Kartoffelgratin", 2,
        [new RecipeLine("kartoffeln", new Quantity(300, Unit.Gramm)), new RecipeLine("Sahne", new Quantity(200, Unit.Milliliter))], []);

    private Guid ListWithBoth()
    {
        Recipes.Save(Soup);
        Recipes.Save(Gratin);
        var page = Render<AddRecipes>(p => p.Add(x => x.Id, Shopping.NewList("Wocheneinkauf")));
        // Found again after each click: a click re-renders the page.
        for (var i = 0; i < page.FindAll("[data-testid=choose]").Count; i++) page.FindAll("[data-testid=choose]")[i].Click();
        page.Find("[data-testid=add]").Click();
        return Shopping.Lists.Single().Id;
    }

    [Fact]
    public void Choosing_two_recipes_puts_the_same_ingredient_on_the_list_once_with_the_sum()
    {
        var id = ListWithBoth();

        var list = Render<ShoppingListPage>(p => p.Add(x => x.Id, id));

        var potatoes = list.FindAll("[data-testid=line]").Single(l => l.TextContent.Contains("Kartoffeln"));
        Assert.Contains("800 g", potatoes.TextContent);
        Assert.Contains("für Kartoffelgratin, Kartoffelsuppe", potatoes.TextContent);
    }

    [Fact]
    public void The_servings_chosen_scale_what_goes_on_the_list()
    {
        Recipes.Save(Soup);
        var page = Render<AddRecipes>(p => p.Add(x => x.Id, Shopping.NewList()));
        page.Find("[data-testid=choose]").Click();
        for (var i = 0; i < 4; i++) page.Find("[aria-label='Mehr Portionen']").Click(); // 4 → 8 servings
        page.Find("[data-testid=add]").Click();

        var line = Assert.Single(Shopping.Lines(Shopping.Lists[0].Id), l => l.Key == "kartoffeln");
        Assert.Equal([new Quantity(1, Unit.Kilogramm)], line.Missing);
    }

    [Fact]
    public void Ticking_moves_a_line_into_the_trolley_and_removing_the_ticked_ones_clears_them()
    {
        var id = ListWithBoth();
        var list = Render<ShoppingListPage>(p => p.Add(x => x.Id, id));

        list.FindAll("[data-testid=line]").Single(l => l.TextContent.Contains("Salz")).Click();

        Assert.Contains("Salz", list.Find("[data-testid=checked]").TextContent);
        list.Find("[data-testid=remove-checked]").Click();
        Assert.DoesNotContain(Shopping.Lines(id), l => l.Key == "salz");
    }

    [Fact]
    public void Something_added_by_hand_joins_the_list()
    {
        var id = Shopping.NewList();
        var list = Render<ShoppingListPage>(p => p.Add(x => x.Id, id));

        list.Find("[data-testid=by-hand]").Change("Milch");
        list.Find("form").Submit();

        Assert.Equal("Milch", Assert.Single(Shopping.Lines(id)).Name);
    }

    [Fact]
    public void Moving_an_ingredient_to_another_aisle_reorders_lists_already_made()
    {
        var id = ListWithBoth();
        var aisles = Render<IngredientsPage>();

        var sahne = Recipes.Ingredients.Select((ingredient, index) => (ingredient, index)).Single(x => x.ingredient.Name == "Sahne").index;
        aisles.FindAll("[data-testid=aisle]")[sahne].Change(nameof(Aisle.ObstGemuese));

        Assert.Equal(Aisle.ObstGemuese, Assert.Single(Shopping.Lines(id), l => l.Key == "sahne").Aisle);
    }

    [Fact]
    public async Task The_list_reaches_the_shopping_service_and_survives_a_new_phone()
    {
        await Sync.StartAsync();
        var id = ListWithBoth();
        AfterSave();

        await WaitFor(() => !Sync.HasUnsyncedChanges && Sync.Status == SyncStatus.Ready);
        var lists = await Server.ListsAsync();
        Assert.Equal("Wocheneinkauf", Assert.Single(lists.Lists).Title);
        Assert.Equal([new Quantity(800, Unit.Gramm)], Assert.Single(lists.Lines(id), l => l.Key == "kartoffeln").Missing);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}

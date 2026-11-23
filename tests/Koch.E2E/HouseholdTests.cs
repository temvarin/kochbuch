using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Koch.E2E;

/// <summary>
/// Anna's and Ben's phones against the whole system: the app, the gateway, the services and their sync. The memory
/// stores live for the whole class, so every test uses recipe titles of its own.
/// </summary>
[Collection(UsesHousehold.Name)]
[Trait("Category", "E2E")]
public class HouseholdTests(Household household)
{
    private static async Task CreateRecipeAsync(Phone phone, string title, int servings, params string[] lines)
    {
        await phone.GoToAsync("Rezepte");
        await phone.Page.GetByRole(AriaRole.Link, new() { Name = "Neues Rezept" }).ClickAsync();
        await Phone.SetAsync(phone.Page.GetByTestId("title"), title);
        await Phone.SetAsync(phone.Page.GetByTestId("servings"), servings.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var line in lines) await phone.AddLineAsync(line);
        await Phone.SetAsync(phone.Page.GetByTestId("step").Last, "Kochen.");
        await phone.SaveRecipeAsync(title);
    }

    [Fact]
    public async Task Edits_to_one_recipe_on_both_phones_one_of_them_offline_both_survive()
    {
        await using var anna = await household.PhoneAsync(Household.Anna);
        await using var ben = await household.PhoneAsync(Household.Ben);
        await anna.OpenAsync();
        await ben.OpenAsync();

        await CreateRecipeAsync(anna, "Linsensuppe", 4, "250 g Linsen", "200 g Karotten");
        await anna.SyncAsync();
        await ben.SyncAsync();
        await ben.OpenRecipeAsync("Linsensuppe");
        Assert.Equal(2, (await ben.IngredientsAsync()).Count);

        // Ben is in the cellar without a network and adds pepper; Anna, online, renames the soup.
        await ben.OfflineAsync();
        await ben.EditRecipeAsync();
        await ben.AddLineAsync("Pfeffer");
        await ben.SaveRecipeAsync("Linsensuppe");

        await anna.OpenRecipeAsync("Linsensuppe");
        await anna.EditRecipeAsync();
        await Phone.SetAsync(anna.Page.GetByTestId("title"), "Annas Linsensuppe");
        await anna.SaveRecipeAsync("Annas Linsensuppe");
        await anna.SyncAsync();

        await ben.OfflineAsync(false);
        await ben.SyncAsync();
        await anna.SyncAsync();

        foreach (var phone in new[] { anna, ben })
        {
            await phone.OpenRecipeAsync("Annas Linsensuppe");
            var ingredients = await phone.IngredientsAsync();
            Assert.Contains(ingredients, i => i.Contains("Linsen", StringComparison.Ordinal));
            Assert.Contains(ingredients, i => i.Contains("Pfeffer", StringComparison.Ordinal));
            Assert.Equal(3, ingredients.Count);
        }
    }

    [Fact]
    public async Task Two_recipes_on_the_list_add_up_and_a_tick_shows_on_the_other_phone()
    {
        await using var anna = await household.PhoneAsync(Household.Anna);
        await using var ben = await household.PhoneAsync(Household.Ben);
        await anna.OpenAsync();
        await CreateRecipeAsync(anna, "Kartoffelsalat", 4, "500 g Kartoffeln");
        await AssertAccessibleAsync(anna.Page); // the recipe page: photo tile, ⋯ menu, servings, actions
        await anna.Page.GetByTestId("add-to-list").ClickAsync();
        await anna.Page.GetByTestId("cook").ClickAsync();
        await AssertAccessibleAsync(anna.Page); // the cooking view
        await anna.Page.GoBackAsync();
        await CreateRecipeAsync(anna, "Bratkartoffeln", 2, "300 g Kartoffeln");
        await anna.Page.GetByTestId("add-to-list").ClickAsync();

        await anna.GoToAsync("Einkauf");
        var potatoes = anna.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Kartoffeln 800 g" });
        await Expect(potatoes).ToBeVisibleAsync();
        await anna.SyncAsync();

        await ben.OpenAsync();
        await ben.SyncAsync();
        await ben.GoToAsync("Einkauf");
        await ben.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Kartoffeln 800 g" }).ClickAsync();
        await Expect(ben.Page.GetByTestId("checked").GetByRole(AriaRole.Checkbox, new() { Name = "Kartoffeln 800 g" })).ToBeVisibleAsync();
        await ben.SyncAsync();

        await anna.SyncAsync();
        await anna.GoToAsync("Einkauf");
        await Expect(anna.Page.GetByTestId("checked").GetByRole(AriaRole.Checkbox, new() { Name = "Kartoffeln 800 g" })).ToBeVisibleAsync();
        await anna.Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "list-on-annas-phone.png") });
    }

    public static TheoryData<string> Pages => ["", "rezept/neu", "einkauf", "konto"];

    /// <summary>WCAG 2.1 AA as axe checks it: nothing serious on the main pages (same rule set as fallstrick).</summary>
    [Theory, MemberData(nameof(Pages))]
    public async Task The_main_pages_have_no_serious_accessibility_problems(string path)
    {
        await using var anna = await household.PhoneAsync(Household.Anna);
        await anna.OpenAsync(path);

        await AssertAccessibleAsync(anna.Page);
    }

    private static async Task AssertAccessibleAsync(IPage page)
    {
        var result = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] },
        });

        var serious = result.Violations.Where(v => v.Impact is "serious" or "critical")
            .Select(v => $"{page.Url} {v.Id}: {v.Help} ({string.Join(", ", v.Nodes.Select(n => n.Target))})");
        Assert.Empty(serious);
    }
}

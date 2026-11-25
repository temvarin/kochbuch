using Koch.App.Account;
using Koch.App.Recipes;
using Koch.App.Shell;
using Koch.App.Shopping;
using Koch.App.Sync;
using Koch.Domain.Measures;
using Koch.Domain.Recipes;
using Microsoft.Extensions.DependencyInjection;

namespace Koch.App.Tests;

public sealed class LocalizationTests : AppTestContext
{
    private static readonly Guid SoupId = new("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80");

    [Theory]
    [InlineData(1, "1 порция")]
    [InlineData(2, "2 порции")]
    [InlineData(4, "4 порции")]
    [InlineData(5, "5 порций")]
    [InlineData(11, "11 порций")]
    [InlineData(21, "21 порция")]
    [InlineData(22, "22 порции")]
    [InlineData(25, "25 порций")]
    public void Russian_servings_use_all_three_plural_forms(int count, string expected)
    {
        var names = Services.GetRequiredService<UnitNames>();
        names.Choose(UnitLanguage.Russian);
        var text = Services.GetRequiredService<AppText>();

        Assert.Equal(expected, text.Servings(count));
    }

    [Fact]
    public void Switching_to_russian_renders_all_pages_in_russian()
    {
        Services.GetRequiredService<UnitNames>().Choose(UnitLanguage.Russian);
        var recipe = new Recipe(SoupId, "Борщ", 5,
            [new RecipeLine("картофель", new Quantity(500, Unit.Gramm)), new RecipeLine("соль", null)],
            ["Нарезать картофель.", "Добавить соль."]);
        Recipes.Save(recipe);
        Shopping.Add(recipe, 5);

        var list = Render<RecipeList>();
        Assert.Contains("Что приготовим сегодня?", list.Markup);
        Assert.Contains("5 порций", list.Markup);
        Assert.Contains("Список покупок", list.Markup);
        Assert.Contains("осталось 2", list.Markup);

        var view = Render<RecipeView>(p => p.Add(x => x.Id, SoupId));
        Assert.Contains("Ингредиенты", view.Markup);
        Assert.Contains("Приготовление", view.Markup);
        Assert.Contains("В список", view.Markup);
        Assert.Contains("Готовить", view.Markup);

        var cook = Render<CookMode>(p => p.Add(x => x.Id, SoupId));
        Assert.Contains("Шаг 1 из 2", cook.Markup);
        Assert.Contains("Для этого шага", cook.Markup);
        Assert.Contains("Дальше", cook.Markup);

        var shopping = Render<ShoppingPage>();
        Assert.Contains("Список покупок", shopping.Markup);
        Assert.Contains("для: Борщ", shopping.Markup);

        var edit = Render<RecipeEdit>();
        edit.Find("[data-testid=new-amount]").Change("bad");
        edit.Find("[data-testid=new-ingredient]").Input("соль");
        edit.Find("[data-testid=save]").Click();
        Assert.Contains("Не удалось распознать количество", edit.Find("[data-testid=error]").TextContent);
    }

    [Fact]
    public void Choosing_russian_on_the_account_page_updates_the_ui_at_once()
    {
        var page = Render<AccountPage>();
        Assert.Contains("Sprache", page.Markup);
        Assert.Contains("Wird geprüft …", page.Markup);

        page.Find("[data-testid=unit-language]").Change(nameof(UnitLanguage.Russian));

        Assert.Contains("Язык", page.Markup);
        Assert.Contains("Проверка…", page.Markup);
    }

    [Fact]
    public void Sync_status_uses_russian_time_forms_when_set_to_russian()
    {
        var sync = Services.GetRequiredService<SyncOverview>();
        var text = Services.GetRequiredService<AppText>();
        Services.GetRequiredService<UnitNames>().Choose(UnitLanguage.Russian);

        Assert.Equal("Ещё не синхронизировалось", text.DescribeSync(sync, Clock.GetUtcNow()));
    }
}

namespace Koch.Recipes.Domain.Tests;

public class CatalogTests
{
    [Theory]
    [InlineData("Kartoffeln", "kartoffeln")]
    [InlineData("Kartoffeln", "  KARTOFFELN ")]
    [InlineData("rote Zwiebeln", "Rote   Zwiebeln")]
    [InlineData("Öl", "öl")]
    public void Names_that_differ_only_in_case_or_spacing_are_one_ingredient(string a, string b)
    {
        Assert.Equal(IngredientKey.Of(a), IngredientKey.Of(b));
    }

    [Fact]
    public void Different_names_are_different_ingredients_and_keys_are_stable_uuids()
    {
        var potatoes = IngredientKey.Of("Kartoffeln");

        Assert.NotEqual(potatoes, IngredientKey.Of("Kartoffel"));
        Assert.Equal(8, potatoes.Version);
        // The key is part of the stored data and the wire: it must never change between versions or devices.
        Assert.Equal(potatoes, IngredientKey.Of("Kartoffeln"));
    }

    [Fact]
    public void The_suggestions_are_every_ingredient_the_recipes_name_once_by_name()
    {
        var book = RecipeBook.Empty.Save(Known.Soup())
            .Save(Known.Recipe(Guid.NewGuid(), "Bratkartoffeln", 2, new RecipeLine("kartoffeln", null), new RecipeLine("Butter", null)));

        // Ordinal, as the app has no culture data: "Öl" after "Salz". The browser filters the list while typing anyway.
        Assert.Equal(["Butter", "Kartoffeln", "Knoblauch", "Salz", "Öl"], book.IngredientNames);
    }

    [Fact]
    public void Saving_again_replaces_the_recipe_and_removing_removes_it()
    {
        var book = RecipeBook.Empty.Save(Known.Soup());
        var renamed = new Recipe(Known.SoupId, "Suppe", 2, [], []);

        book = book.Save(renamed);
        Assert.Equal(renamed, Assert.Single(book.Recipes));

        book = book.Remove(Known.SoupId);
        Assert.Empty(book.Recipes);
        Assert.Empty(book.IngredientNames);
    }

    [Fact]
    public void A_book_refuses_two_recipes_with_one_id()
    {
        Assert.Throws<ArgumentException>(() => new RecipeBook([Known.Soup(), Known.Soup()]));
    }
}

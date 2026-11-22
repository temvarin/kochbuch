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
    public void Saving_a_recipe_adds_the_ingredients_the_catalogue_lacks_once()
    {
        var book = RecipeBook.Empty.Save(new Ingredient("Kartoffeln", Aisle.ObstGemuese)).Save(Known.Soup());

        Assert.Equal(["Kartoffeln", "Knoblauch", "Öl", "Salz"], book.Ingredients.Select(i => i.Name));
        Assert.Equal(Aisle.ObstGemuese, book.Ingredients[0].Aisle); // known ones keep their aisle
        Assert.Equal(Aisle.Sonstiges, book.Ingredients[1].Aisle);
    }

    [Fact]
    public void Saving_again_replaces_the_recipe_and_removing_keeps_the_catalogue()
    {
        var book = RecipeBook.Empty.Save(Known.Soup());
        var renamed = new Recipe(Known.SoupId, "Suppe", 2, [], []);

        book = book.Save(renamed);
        Assert.Equal(renamed, Assert.Single(book.Recipes));

        book = book.Remove(Known.SoupId);
        Assert.Empty(book.Recipes);
        Assert.Equal(4, book.Ingredients.Count);
    }

    [Fact]
    public void A_book_refuses_two_recipes_with_one_id()
    {
        Assert.Throws<ArgumentException>(() => new RecipeBook([Known.Soup(), Known.Soup()], []));
        Assert.Throws<ArgumentException>(() => new RecipeBook([],
            [new Ingredient("Salz", Aisle.Gewuerze), new Ingredient("salz", Aisle.Vorrat)]));
    }
}

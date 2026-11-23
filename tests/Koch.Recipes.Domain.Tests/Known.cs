namespace Koch.Recipes.Domain.Tests;

/// <summary>Fixed data for tests and contract samples: the same every run.</summary>
public static class Known
{
    public static readonly Guid SoupId = new("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80");
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 18, 30, 0, TimeSpan.Zero);

    public static Recipe Soup() => new(SoupId, "Kartoffelsuppe", 4,
        [
            new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm), "festkochend"),
            new RecipeLine("Knoblauch", new Quantity(1, Unit.Zehe)),
            new RecipeLine("Öl", new Quantity(2, Unit.Essloeffel)),
            new RecipeLine("Salz", null),
        ],
        ["Kartoffeln schälen und würfeln.", "Mit Knoblauch in Öl anschwitzen,\nmit Wasser aufgießen, 20 Minuten kochen."],
        "Schmeckt am nächsten Tag noch besser.",
        new Guid("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f"));

    public static RecipeBook Book() =>
        RecipeBook.Empty.Save(Soup());

    public static Recipe Recipe(Guid id, string title = "Rezept", int servings = 2, params RecipeLine[] lines) =>
        new(id, title, servings, lines, []);
}

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koch.Kernel;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Catalog;
using Koch.Recipes.Domain.Cooking;
using Koch.Recipes.Domain.Sync;
using Koch.Recipes.Storage;
using Koch.ServiceDefaults.Documents;

namespace Koch.Recipes.Tests;

/// <summary>
/// Stored rows are never migrated: every later version of the service must read every row an earlier one wrote
/// (tests/Koch.Recipes.Domain.Tests/Contracts/README.md). This version writes exactly the newest sample of each, and
/// reads every sample back to the same book.
/// </summary>
public class RowContractTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Contracts");

    private static RecipeBook Book() => RecipeBook.Empty
        .Save(new Recipe(new Guid("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"), "Kartoffelsuppe", 4,
            [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm), "festkochend"), new RecipeLine("Salz", null)],
            ["Kartoffeln schälen und würfeln.", "Kochen."], "Schmeckt am nächsten Tag noch besser."))
        .Save(new Ingredient("Kartoffeln", Aisle.ObstGemuese));

    private static string Written(string name) => name switch
    {
        "row-recipe" => Rows.For(BookChanges.Between(RecipeBook.Empty, Book())).Single(r => r.Id.StartsWith("recipe:", StringComparison.Ordinal)).Json!,
        "row-ingredient" => Rows.For(BookChanges.Between(RecipeBook.Empty, Book())).Single(r => r.Json!.Contains("Kartoffeln", StringComparison.Ordinal) && r.Id.StartsWith("ingredient:", StringComparison.Ordinal)).Json!,
        _ => throw new ArgumentException(name),
    };

    public static TheoryData<string> Names => ["row-recipe", "row-ingredient"];

    [Theory, MemberData(nameof(Names))]
    public void This_version_writes_exactly_the_newest_sample(string name)
    {
        var newest = Directory.Exists(Path.Combine(Folder, name))
            ? Directory.GetFiles(Path.Combine(Folder, name), "v*.json").Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        var written = Written(name);
        if (newest is not null && JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(newest)), JsonNode.Parse(written))) return;

        var received = Path.Combine(SourceFolder(), name, "received.json");
        Directory.CreateDirectory(Path.GetDirectoryName(received)!);
        File.WriteAllText(received, JsonNode.Parse(written)!.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Fail($"The service writes {name} differently from its newest sample. Rows may only gain optional fields; "
            + $"if that is what changed, add {received} as Contracts/{name}/v<n>-<yyyy-MM-dd>.json.");
    }

    [Fact]
    public void Every_row_sample_reads_back_to_the_same_book()
    {
        var recipes = Directory.GetFiles(Path.Combine(Folder, "row-recipe"), "v*.json");
        var ingredients = Directory.GetFiles(Path.Combine(Folder, "row-ingredient"), "v*.json");
        Assert.NotEmpty(recipes);
        Assert.NotEmpty(ingredients);

        foreach (var (recipe, ingredient) in recipes.Zip(ingredients))
        {
            var book = Rows.ToBook([
                new StoredRow("recipe:x", File.ReadAllText(recipe)),
                new StoredRow("ingredient:x", File.ReadAllText(ingredient)),
            ]);
            Assert.Equal(Book().Recipes, book.Recipes);
            Assert.Equal("Kartoffeln", Assert.Single(book.Ingredients).Name);
        }
    }

    private static string SourceFolder([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "Contracts");
}

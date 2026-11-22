using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koch.Recipes.Contracts;
using Koch.Recipes.Domain.Storage;
using Koch.Recipes.Domain.Wire;

namespace Koch.Recipes.Domain.Tests;

/// <summary>
/// The data that outlives a version of the app or the service: one version writes it, another reads it
/// (Contracts/README.md). Each sample in Contracts/ is what a released version wrote. This version must write exactly
/// the newest sample of each contract, and read every sample and write it again unchanged. Round trips alone can't
/// check this: they write and read with the same code, so a renamed property passes them. Ported from kassensturz.
/// </summary>
public class ContractTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Contracts");

    private sealed record Contract(string Name, int Version, Func<string> Write, Func<string, string?> Reread);

    private static readonly Contract[] Contracts =
    [
        new("local", BookStore.Schema, () => BookStore.Serialize(LocalBook()),
            json => BookStore.TryRead(json) is { } book ? BookStore.Serialize(book) : null),
        new("sync-request", SyncRequestJson.CurrentProtocol,
            // Since protocol 3 with a base: the soup as the phone last got it, so the service can merge.
            () => JsonSerializer.Serialize(BookChanges.Between(RecipeBook.Empty, Known.Book()).ToJson([Known.Soup()]), RecipesJson.Default.SyncRequestJson),
            json => JsonSerializer.Deserialize(json, RecipesJson.Default.SyncRequestJson) is { } request
                ? JsonSerializer.Serialize(request.ToDomain().ToJson(request.BasesToDomain()), RecipesJson.Default.SyncRequestJson)
                : null),
        new("sync-response", SyncRequestJson.CurrentProtocol,
            () => JsonSerializer.Serialize(Known.Book().ToJson(), RecipesJson.Default.SyncResponseJson),
            json => JsonSerializer.Serialize(JsonSerializer.Deserialize(json, RecipesJson.Default.SyncResponseJson)!.ToDomain().ToJson(), RecipesJson.Default.SyncResponseJson)),
    ];

    /// <summary>A phone after one sync, with an unanswered change since.</summary>
    private static SyncedBook LocalBook()
    {
        var phone = new SyncedBook();
        phone.Change(book => book.Save(Known.Soup()));
        phone.CompleteSync(phone.StartSync(), Known.Book(), Known.Now);
        phone.Change(book => book.Remove(Known.SoupId));
        phone.SyncFailed(phone.StartSync());
        return phone;
    }

    public static TheoryData<string> Names => [.. Contracts.Select(c => c.Name)];

    public static TheoryData<string, string> Samples()
    {
        var samples = new TheoryData<string, string>();
        foreach (var contract in Contracts)
        {
            foreach (var path in SamplesOf(contract.Name)) samples.Add(contract.Name, Path.GetFileName(path));
        }
        return samples;
    }

    [Theory, MemberData(nameof(Names))]
    public void This_version_writes_exactly_the_newest_sample(string name)
    {
        var contract = Contracts.Single(c => c.Name == name);
        var written = contract.Write();
        var newest = SamplesOf(name).LastOrDefault();
        if (newest is not null && Version(newest) == contract.Version
            && JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(newest)), JsonNode.Parse(written)))
            return;

        var received = Path.Combine(SourceFolder(), name, "received.json");
        Directory.CreateDirectory(Path.GetDirectoryName(received)!);
        File.WriteAllText(received, Pretty(written));
        Assert.Fail($"""
            This version writes {name} differently from its newest sample ({(newest is null ? "none yet" : Path.GetFileName(newest))}).
            If that is intended: make sure every older sample still passes, and raise the version if one can't. Then add
              {received}
            as Contracts/{name}/v{contract.Version}-<yyyy-MM-dd>.json. Never edit a sample that was released.
            """);
    }

    [Theory, MemberData(nameof(Samples))]
    public void Every_sample_stays_readable_and_the_current_ones_lose_nothing(string name, string file)
    {
        var contract = Contracts.Single(c => c.Name == name);
        var sample = File.ReadAllText(Path.Combine(Folder, name, file));

        var reread = contract.Reread(sample);

        // Every sample stays readable. Only the current version's must come back unchanged: an older one may lack a
        // field that was added since (and its protocol may be refused by the service, which reading it here allows).
        Assert.NotNull(reread);
        if (Version(Path.Combine(Folder, name, file)) == contract.Version)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(sample), JsonNode.Parse(reread)), $"{name}/{file} changed when read and written again.");
    }

    private static IEnumerable<string> SamplesOf(string name)
    {
        var folder = Path.Combine(Folder, name);
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "v*.json").OrderBy(Version).ThenBy(p => p, StringComparer.Ordinal)
            : [];
    }

    private static int Version(string path) => int.Parse(Path.GetFileName(path)[1..].Split('-')[0], System.Globalization.CultureInfo.InvariantCulture);

    private static string Pretty(string json) => JsonNode.Parse(json)!.ToJsonString(
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // The test project's folder, where samples are kept (the output folder only has copies).
    private static string SourceFolder([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "Contracts");
}

using System.Text.Json;
using Azure.Storage.Blobs;
using Koch.Api.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koch.Services.Tests;

public abstract class DocumentRowsContract
{
    protected abstract IDocumentRows Store { get; }
    protected abstract Task<int> VersionOfAsync(string id);

    [Fact]
    public async Task Written_rows_are_read_back_in_id_order()
    {
        var rows = await Store.WriteAndReadAsync([new("recipe:b", """{"x":2}"""), new("recipe:a", """{"x":1}""")]);

        Assert.Equal([new StoredRow("recipe:a", """{"x":1}"""), new StoredRow("recipe:b", """{"x":2}""")], rows);
    }

    [Fact]
    public async Task A_removed_row_is_no_longer_read_but_can_come_back()
    {
        await Store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]);

        Assert.Empty(await Store.WriteAndReadAsync([new("recipe:a", null)]));
        Assert.Single(await Store.WriteAndReadAsync([new("recipe:a", """{"x":3}""")]));
    }

    [Fact]
    public async Task Writing_the_same_json_again_makes_no_new_version()
    {
        await Store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]);
        await Store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]);
        Assert.Equal(1, await VersionOfAsync("recipe:a"));

        await Store.WriteAndReadAsync([new("recipe:a", """{"x":2}""")]);
        Assert.Equal(2, await VersionOfAsync("recipe:a"));
    }

    [Fact]
    public async Task A_change_of_case_only_is_a_change()
    {
        await Store.WriteAndReadAsync([new("recipe:a", """{"x":"salz"}""")]);

        var rows = await Store.WriteAndReadAsync([new("recipe:a", """{"x":"Salz"}""")]);

        Assert.Equal("""{"x":"Salz"}""", Assert.Single(rows).Json);
        Assert.Equal(2, await VersionOfAsync("recipe:a"));
    }

    [Fact]
    public async Task Removing_what_was_never_there_is_harmless()
    {
        Assert.Empty(await Store.WriteAndReadAsync([new("recipe:gone", null)]));
    }

    [Fact]
    public async Task A_merge_runs_on_the_row_as_stored_and_never_on_a_missing_or_removed_one()
    {
        await Store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:gone", "{}")]);
        await Store.WriteAndReadAsync([new("recipe:gone", null)]);
        string? seen = null;

        var rows = await Store.WriteAndReadAsync(
        [
            new("recipe:a", """{"x":2}""", stored => { seen = stored; return """{"x":3}"""; }),
            new("recipe:b", """{"y":1}""", _ => throw new InvalidOperationException("Nothing to merge with.")),
            new("recipe:gone", """{"z":1}""", _ => throw new InvalidOperationException("Removed: nothing to merge with.")),
        ]);

        Assert.Equal("""{"x":1}""", seen);
        Assert.Equal(["""{"x":3}""", """{"y":1}""", """{"z":1}"""], rows.Select(r => r.Json));
    }
}

public sealed class MemoryDocumentRowsTests : DocumentRowsContract
{
    private readonly MemoryDocumentRows store = new();
    protected override IDocumentRows Store => store;
    protected override Task<int> VersionOfAsync(string id) => Task.FromResult(store.VersionOf(id));
}

public sealed class FileDocumentRowsTests : DocumentRowsContract, IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"koch-rows-{Guid.NewGuid():N}", "recipes.json");
    private readonly FileDocumentRows store;

    public FileDocumentRowsTests() => store = new FileDocumentRows(path);

    protected override IDocumentRows Store => store;
    protected override Task<int> VersionOfAsync(string id) => Task.FromResult(store.VersionOf(id));

    [Fact]
    public async Task A_failing_request_saves_none_of_its_rows()
    {
        await Assert.ThrowsAnyAsync<JsonException>(() =>
            Store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:b", "not json")]));

        Assert.Empty(await Store.WriteAndReadAsync([]));
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

/// <summary>Runs against Azurite when KOCH_TEST_STORAGE is set (e.g. in CI).</summary>
public sealed class BlobDocumentRowsTests : DocumentRowsContract, IAsyncLifetime
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("KOCH_TEST_STORAGE");

    private BlobContainerClient? container;
    private BlobDocumentRows? blobStore;
    private readonly MemoryDocumentRows memory = new();

    protected override IDocumentRows Store => (IDocumentRows?)blobStore ?? memory;

    protected override Task<int> VersionOfAsync(string id) =>
        blobStore is not null ? blobStore.VersionOfAsync(id) : Task.FromResult(memory.VersionOf(id));

    public async Task InitializeAsync()
    {
        if (ConnectionString is not { Length: > 0 }) return;
        container = new BlobServiceClient(ConnectionString).GetBlobContainerClient($"test-{Guid.NewGuid():N}");
        await container.CreateIfNotExistsAsync();
        blobStore = new BlobDocumentRows(container, "recipes.json", NullLogger<BlobDocumentRows>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DeleteIfExistsAsync();
    }

    [Fact]
    public async Task CanRead_checks_whether_the_container_exists()
    {
        if (blobStore is null || container is null) return;
        Assert.True(await blobStore.CanReadAsync());

        await container.DeleteIfExistsAsync();
        Assert.False(await blobStore.CanReadAsync());
    }

    [Fact]
    public async Task A_failing_request_saves_none_of_its_rows()
    {
        if (blobStore is null) return;

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            blobStore.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:b", "not json")]));

        Assert.Empty(await blobStore.WriteAndReadAsync([]));
    }
}

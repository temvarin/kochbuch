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

public sealed class SqlDocumentRowsTests : DocumentRowsContract, IAsyncLifetime
{
    private readonly SqlDatabase sql = new();

    public async Task InitializeAsync()
    {
        await sql.InitializeAsync();
        await sql.ExecuteAsync(SqlDatabase.SchemaScript());
    }

    public Task DisposeAsync() => sql.DisposeAsync();

    protected override IDocumentRows Store => new SqlDocumentRows(sql.ConnectionString, "recipes", NullLogger<SqlDocumentRows>.Instance);

    protected override async Task<int> VersionOfAsync(string id) =>
        (int)(await sql.ExecuteAsync($"SELECT Version FROM recipes.Documents WHERE Id = '{id}'") ?? 0);

    [Fact]
    public void A_schema_name_is_never_anything_but_letters()
    {
        Assert.Throws<ArgumentException>(() =>
            new SqlDocumentRows(sql.ConnectionString, "recipes; DROP TABLE x", NullLogger<SqlDocumentRows>.Instance));
    }

    [Fact]
    public async Task A_failing_request_saves_none_of_its_rows()
    {
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            Store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:b", "not json")]));

        Assert.Equal(0, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Documents"));
    }
}

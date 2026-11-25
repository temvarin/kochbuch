using Koch.Api.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koch.Services.Tests;

public sealed class DatabaseTests : IAsyncLifetime
{
    private readonly SqlDatabase sql = new();

    public Task InitializeAsync() => sql.InitializeAsync();
    public Task DisposeAsync() => sql.DisposeAsync();

    private SqlDocumentRows Rows(string schema) => new(sql.ConnectionString, schema, NullLogger<SqlDocumentRows>.Instance);

    [Fact]
    public async Task Schema_script_creates_both_tables_and_is_idempotent()
    {
        Assert.False(await Rows("recipes").CanReadAsync());
        Assert.False(await Rows("shopping").CanReadAsync());

        await sql.ExecuteAsync(SqlDatabase.SchemaScript());
        await sql.ExecuteAsync("INSERT INTO recipes.Documents (Id, Json, Version) VALUES ('recipe:1', '{}', 1)");
        await sql.ExecuteAsync("INSERT INTO shopping.Documents (Id, Json, Version) VALUES ('item:1', '{}', 1)");
        await sql.ExecuteAsync(SqlDatabase.SchemaScript());

        Assert.True(await Rows("recipes").CanReadAsync());
        Assert.True(await Rows("shopping").CanReadAsync());
        Assert.Equal(1, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Documents"));
        Assert.Equal(1, await sql.ExecuteAsync("SELECT COUNT(*) FROM shopping.Documents"));
    }

    [Fact]
    public async Task Rows_must_be_json_and_their_old_versions_are_kept_in_history()
    {
        await sql.ExecuteAsync(SqlDatabase.SchemaScript());
        await sql.ExecuteAsync("INSERT INTO recipes.Documents (Id, Json, Version) VALUES ('recipe:1', '{\"v\":1}', 1)");
        await sql.ExecuteAsync("UPDATE recipes.Documents SET Json = '{\"v\":2}', Version = 2 WHERE Id = 'recipe:1'");

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            sql.ExecuteAsync("INSERT INTO recipes.Documents (Id, Json, Version) VALUES ('recipe:2', 'not json', 1)"));
        Assert.Equal(1, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.DocumentsHistory"));
    }
}

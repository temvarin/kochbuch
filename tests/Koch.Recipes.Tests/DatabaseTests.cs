using Koch.ServiceDefaults.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koch.Recipes.Tests;

/// <summary>The real scripts in db/recipes against a real SQL Server.</summary>
public sealed class DatabaseTests : IAsyncLifetime
{
    private readonly SqlDatabase sql = new();

    public Task InitializeAsync() => sql.InitializeAsync();

    public Task DisposeAsync() => sql.DisposeAsync();

    private SqlDocumentRows Database() => new(sql.ConnectionString, "recipes", NullLogger<SqlDocumentRows>.Instance);

    private async Task ApplyScripts()
    {
        foreach (var script in SqlDatabase.Scripts("recipes")) await sql.ExecuteAsync(script);
    }

    [Fact]
    public async Task Without_the_scripts_the_service_reports_its_tables_unreadable()
    {
        Assert.False(await Database().CanReadAsync());
    }

    [Fact]
    public async Task After_the_scripts_the_service_can_read_its_tables()
    {
        await ApplyScripts();

        Assert.True(await Database().CanReadAsync());
    }

    [Fact]
    public async Task The_scripts_can_run_again_on_every_deploy()
    {
        await ApplyScripts();
        await sql.ExecuteAsync("INSERT INTO recipes.Documents (Id, Json, Version) VALUES ('recipe:1', '{}', 1)");

        await ApplyScripts();

        Assert.Equal(1, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Documents"));
    }

    [Fact]
    public async Task Rows_must_be_json_and_their_old_versions_are_kept()
    {
        await ApplyScripts();
        await sql.ExecuteAsync("INSERT INTO recipes.Documents (Id, Json, Version) VALUES ('recipe:1', '{\"v\":1}', 1)");
        await sql.ExecuteAsync("UPDATE recipes.Documents SET Json = '{\"v\":2}', Version = 2 WHERE Id = 'recipe:1'");

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            sql.ExecuteAsync("INSERT INTO recipes.Documents (Id, Json, Version) VALUES ('recipe:2', 'not json', 1)"));
        Assert.Equal(1, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.DocumentsHistory"));
    }
}

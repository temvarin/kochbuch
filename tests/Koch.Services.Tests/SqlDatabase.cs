using Microsoft.Data.SqlClient;

namespace Koch.Services.Tests;

public sealed class SqlDatabase : IAsyncLifetime
{
    private static readonly string Server = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("KOCH_TEST_SQL") ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true")
    { TrustServerCertificate = true }.ConnectionString;

    private readonly string name = $"KochTest_{Guid.NewGuid():N}";

    public string ConnectionString => new SqlConnectionStringBuilder(Server) { InitialCatalog = name }.ConnectionString;

    public static string SchemaScript() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "db", "schema.sql"));

    public async Task InitializeAsync() => await ExecuteAsync(Server, $"CREATE DATABASE [{name}]");

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(Server, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
    }

    public Task<object?> ExecuteAsync(string sql) => ExecuteAsync(ConnectionString, sql);

    private static async Task<object?> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}

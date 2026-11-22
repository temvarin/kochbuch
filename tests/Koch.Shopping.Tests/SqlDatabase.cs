using Microsoft.Data.SqlClient;

namespace Koch.Shopping.Tests;

/// <summary>
/// A fresh, empty database, deleted afterwards. On SQL Server LocalDB, or on the server in the environment variable
/// KOCH_TEST_SQL (CI runs one in a container). Ported from kassensturz.
/// </summary>
public sealed class SqlDatabase : IAsyncLifetime
{
    private static readonly string Server = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("KOCH_TEST_SQL") ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true")
    { TrustServerCertificate = true }.ConnectionString;

    private readonly string name = $"KochTest_{Guid.NewGuid():N}";

    public string ConnectionString => new SqlConnectionStringBuilder(Server) { InitialCatalog = name }.ConnectionString;

    /// <summary>The service's scripts in the order CI applies them (by file name).</summary>
    public static IEnumerable<string> Scripts(string service) =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "db", service), "*.sql")
            .Order(StringComparer.Ordinal).Select(File.ReadAllText);

    public async Task InitializeAsync() => await ExecuteAsync(Server, $"CREATE DATABASE [{name}]");

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(Server, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
    }

    /// <summary>Runs SQL on this database and returns the first value of the result.</summary>
    public Task<object?> ExecuteAsync(string sql) => ExecuteAsync(ConnectionString, sql);

    private static async Task<object?> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}

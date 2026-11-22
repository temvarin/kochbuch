using Microsoft.Data.SqlClient;

// Usage: dotnet run --project tools/Koch.DbDeploy -- db/recipes [db/shopping ...]
// The connection string comes from KOCH_DB_CONNECTION, so that it never shows up in a log. In CI it uses
// "Authentication=Active Directory Default", which picks up the identity azure/login signed in with.
var connectionString = Environment.GetEnvironmentVariable("KOCH_DB_CONNECTION")
    ?? throw new InvalidOperationException("Set KOCH_DB_CONNECTION.");
if (args.Length == 0) throw new InvalidOperationException("Name at least one folder of scripts, e.g. db/recipes.");

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
foreach (var folder in args)
{
    foreach (var file in Directory.GetFiles(folder, "*.sql").Order(StringComparer.Ordinal))
    {
        Console.WriteLine($"Applying {file}");
        await using var command = new SqlCommand(await File.ReadAllTextAsync(file), connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}
Console.WriteLine("Database scripts applied.");

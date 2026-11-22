using System.Data;
using System.Text.RegularExpressions;
using Koch.Telemetry;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Koch.ServiceDefaults.Documents;

/// <summary>A stored row: one synced item as JSON, e.g. <c>recipe:{id}</c>.</summary>
public sealed record StoredRow(string Id, string Json);

/// <summary>A row to write.</summary>
/// <param name="Json">null: the item was removed. The row stays as a tombstone, so its history is kept.</param>
/// <param name="MergeWith">
/// For a row that is live when written: what to write instead, given the JSON stored now. Runs under the row's lock
/// inside the transaction, so it merges with the latest version, never with one another request is replacing
/// (DDIA ch08 p.300, read-modify-write; docs/adr/0012). Not used for removals or for rows that aren't there.
/// </param>
public sealed record RowWrite(string Id, string? Json, Func<string, string>? MergeWith = null);

/// <summary>What a write really changed in one row.</summary>
/// <param name="Before">The live JSON before; null if the row was absent or removed.</param>
/// <param name="After">The live JSON after; null if the write removed it.</param>
public sealed record RowChange(string Id, string? Before, string? After);

/// <summary>
/// The events a service derives from a changed row. They are saved in the same transaction as the row, in the service's
/// Outbox table, and published from there (DDIA ch12 p.507-508; docs/adr/0008).
/// </summary>
public delegate IEnumerable<OutboxEvent> RowEvents(RowChange change);

/// <summary>
/// A service's synced rows in its own schema (db/{service}/001-schema.sql): the same table shape for every service,
/// kassensturz's documents with versions and temporal history (docs/adr/0003, 0004).
/// </summary>
public interface IDocumentRows
{
    /// <returns>true if the service's identity could read its table.</returns>
    Task<bool> CanReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the rows in one transaction, so a sync request is saved whole or not at all, and returns every live row
    /// afterwards. A row written with the JSON it already has is left alone: no new version, nothing in the history, no
    /// event, so a repeated request changes nothing.
    /// </summary>
    Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default);
}

public static class DocumentRows
{
    /// <summary>What a removed item's row holds. Removed rows are left out when reading.</summary>
    public const string Tombstone = """{"removed":true}""";

    /// <summary>The JSON as the service sees it: null for a missing or removed row.</summary>
    public static string? Live(string? json) => json is null or Tombstone ? null : json;

    /// <summary>The JSON to store for <paramref name="write"/> when the row holds <paramref name="before"/>.</summary>
    public static string Target(RowWrite write, string? before) =>
        write.Json is null ? Tombstone
        : write.MergeWith is { } merge && Live(before) is { } current ? merge(current)
        : write.Json;

    /// <summary>The change as the service sees it; null if there is none, e.g. removing what was never there.</summary>
    public static RowChange? Change(string id, string? before, string after) =>
        Live(before) is var was && Live(after) is var now && string.Equals(was, now, StringComparison.Ordinal)
            ? null
            : new RowChange(id, was, now);
}

/// <summary>
/// <see cref="IDocumentRows"/> in Azure SQL. The connection string has no password: in Azure the service logs in with
/// its own managed identity, which may use its schema and nothing else (db/manual/grants.sql).
/// </summary>
public sealed partial class SqlDocumentRows : IDocumentRows
{
    private readonly string connectionString, lockRow, insert, update, insertEvent, readLive, count;
    private readonly RowEvents? events;
    private readonly ILogger<SqlDocumentRows> logger;

    /// <param name="schema">The service's schema. Written into the SQL, so only lower-case letters: never user input.</param>
    /// <param name="events">
    /// The events a change causes, if the service publishes any; its schema then needs an Outbox table
    /// (db/recipes/002-outbox.sql).
    /// </param>
    public SqlDocumentRows(string connectionString, string schema, ILogger<SqlDocumentRows> logger, RowEvents? events = null)
    {
        Sql.CheckSchema(schema);
        (this.connectionString, this.logger, this.events) = (connectionString, logger, events);
        // The row is read with an update lock held to the end of the transaction, also when it doesn't exist yet: two
        // requests for the same row take turns, and the events of one are numbered after the other's (docs/adr/0008).
        lockRow = $"SELECT Json FROM {schema}.Documents WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id";
        insert = $"INSERT INTO {schema}.Documents (Id, Json, Version) VALUES (@Id, @Json, 1)";
        update = $"UPDATE {schema}.Documents SET Json = @Json, Version = Version + 1 WHERE Id = @Id";
        insertEvent = $"INSERT INTO {schema}.Outbox (Type, Subject, Data, TraceParent) VALUES (@Type, @Subject, @Data, @TraceParent)";
        readLive = $"SELECT Id, Json FROM {schema}.Documents WHERE JSON_VALUE(Json, '$.removed') IS NULL ORDER BY Id";
        count = $"SELECT COUNT_BIG(*) FROM {schema}.Documents";
    }

    public async Task<bool> CanReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await Sql.OpenAsync(connectionString, cancellationToken);
            // Reading the table, not SELECT 1: proves the schema exists and the identity was granted it.
            await using var command = new SqlCommand(count, connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (SqlException e)
        {
            TableUnreadable(logger, e);
            return false;
        }
    }

    public async Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(
        IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default)
    {
        await using var connection = await Sql.OpenAsync(connectionString, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var traceParent = (object?)AppTelemetry.TraceParent() ?? DBNull.Value;
        foreach (var write in writes)
        {
            string? before;
            await using (var read = Command(lockRow, connection, transaction, write.Id))
                before = await read.ExecuteScalarAsync(cancellationToken) as string;
            var json = DocumentRows.Target(write, before);
            // Compared here, ordinally: the database's collation ignores case, and "salz" → "Salz" is a change.
            if (string.Equals(before, json, StringComparison.Ordinal)) continue;

            await using (var save = Command(before is null ? insert : update, connection, transaction, write.Id))
            {
                save.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = json;
                await save.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var e in Events(write.Id, before, json))
            {
                await using var add = new SqlCommand(insertEvent, connection, transaction);
                add.Parameters.Add("@Type", SqlDbType.VarChar, 100).Value = e.Type;
                add.Parameters.Add("@Subject", SqlDbType.NVarChar, 200).Value = e.Subject;
                add.Parameters.Add("@Data", SqlDbType.NVarChar, -1).Value = e.Data;
                add.Parameters.Add("@TraceParent", SqlDbType.VarChar, 55).Value = traceParent;
                await add.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        List<StoredRow> rows = [];
        await using (var read = new SqlCommand(readLive, connection, transaction))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new StoredRow(reader.GetString(0), reader.GetString(1)));
        }
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private static SqlCommand Command(string sql, SqlConnection connection, SqlTransaction transaction, string id)
    {
        var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 200).Value = id;
        return command;
    }

    private IEnumerable<OutboxEvent> Events(string id, string? before, string after) =>
        DocumentRows.Change(id, before, after) is { } change && events is not null ? events(change) : [];

    [LoggerMessage(Level = LogLevel.Warning, Message = "The service's table can't be read.")]
    private static partial void TableUnreadable(ILogger logger, Exception exception);
}

/// <summary>What the SQL stores share: connecting, and schema names that are safe to write into SQL.</summary>
internal static partial class Sql
{
    // Azure SQL is briefly unavailable now and then (maintenance, failover). SqlClient retries opening on such errors.
    private static readonly SqlRetryLogicBaseProvider Retry = SqlConfigurableRetryFactory.CreateFixedRetryProvider(
        new SqlRetryLogicOption { NumberOfTries = 3, DeltaTime = TimeSpan.FromSeconds(5) });

    public static void CheckSchema(string schema)
    {
        if (!SchemaName().IsMatch(schema)) throw new ArgumentException($"Not a schema name: '{schema}'.");
    }

    public static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString) { RetryLogicProvider = Retry };
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex SchemaName();
}

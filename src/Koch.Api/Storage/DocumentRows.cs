using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Koch.Api.Storage;

public sealed record StoredRow(string Id, string Json);

public sealed record RowWrite(string Id, string? Json, Func<string, string>? MergeWith = null);

public interface IDocumentRows
{
    Task<bool> CanReadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredRow>> WriteAndReadAsync(IReadOnlyList<RowWrite> writes, CancellationToken cancellationToken = default);
}

public static class DocumentRows
{
    public const string Tombstone = """{"removed":true}""";

    public static string? Live(string? json) => json is null or Tombstone ? null : json;

    public static string Target(RowWrite write, string? before) =>
        write.Json is null ? Tombstone
        : write.MergeWith is { } merge && Live(before) is { } current ? merge(current)
        : write.Json;
}

public sealed partial class SqlDocumentRows : IDocumentRows
{
    private readonly string connectionString, lockRow, insert, update, readLive, count;
    private readonly ILogger<SqlDocumentRows> logger;

    public SqlDocumentRows(string connectionString, string schema, ILogger<SqlDocumentRows> logger)
    {
        Sql.CheckSchema(schema);
        (this.connectionString, this.logger) = (connectionString, logger);
        lockRow = $"SELECT Json FROM {schema}.Documents WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id";
        insert = $"INSERT INTO {schema}.Documents (Id, Json, Version) VALUES (@Id, @Json, 1)";
        update = $"UPDATE {schema}.Documents SET Json = @Json, Version = Version + 1 WHERE Id = @Id";
        readLive = $"SELECT Id, Json FROM {schema}.Documents WHERE JSON_VALUE(Json, '$.removed') IS NULL ORDER BY Id";
        count = $"SELECT COUNT_BIG(*) FROM {schema}.Documents";
    }

    public async Task<bool> CanReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await Sql.OpenAsync(connectionString, cancellationToken);
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
        foreach (var write in writes)
        {
            string? before;
            await using (var read = Command(lockRow, connection, transaction, write.Id))
                before = await read.ExecuteScalarAsync(cancellationToken) as string;
            var json = DocumentRows.Target(write, before);
            if (string.Equals(before, json, StringComparison.Ordinal)) continue;

            await using var save = Command(before is null ? insert : update, connection, transaction, write.Id);
            save.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = json;
            await save.ExecuteNonQueryAsync(cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The service's table can't be read.")]
    private static partial void TableUnreadable(ILogger logger, Exception exception);
}

internal static partial class Sql
{
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

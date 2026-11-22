using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Koch.ServiceDefaults.Documents;

/// <summary>An event to publish, as the service's code describes it.</summary>
/// <param name="Type">The event's type, e.g. <c>koch.recipes.photo.attached</c>.</param>
/// <param name="Subject">What it is about, e.g. <c>photos/{id}</c>.</param>
/// <param name="Data">The event's data as JSON: the published contract, not the row's internal shape (DDIA ch12 p.507).</param>
public sealed record OutboxEvent(string Type, string Subject, string Data);

/// <summary>A saved event not yet published.</summary>
/// <param name="Sequence">
/// Numbered by the database when the event is saved: increasing, with gaps. Two events about the same row are numbered
/// in the order of their transactions, because the row stays locked until commit (docs/adr/0008).
/// </param>
/// <param name="TraceParent">
/// Where in a trace the event was saved (W3C <c>traceparent</c>), so the consumer's work joins the request that caused it
/// (docs/adr/0009); null if it was saved outside a trace.
/// </param>
public sealed record PendingEvent(long Sequence, DateTimeOffset OccurredAt, OutboxEvent Event, string? TraceParent = null);

/// <summary>The events <see cref="SqlDocumentRows"/> saved, for the relay that publishes them.</summary>
public interface IOutbox
{
    /// <summary>The oldest unpublished events, by sequence.</summary>
    Task<IReadOnlyList<PendingEvent>> ReadUnsentAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks events as published. Only these: an event with a lower sequence whose transaction commits later is still
    /// sent next time.
    /// </summary>
    Task MarkSentAsync(IReadOnlyCollection<long> sequences, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IOutbox"/> in the service's Outbox table. Published rows are kept, marked with the time: a few a month, and
/// "was it sent, and when?" is the first question when a photo isn't cleaned up.
/// </summary>
public sealed class SqlOutbox : IOutbox
{
    private readonly string connectionString, readUnsent, markSent;

    /// <param name="schema">The service's schema; only lower-case letters.</param>
    public SqlOutbox(string connectionString, string schema)
    {
        Sql.CheckSchema(schema);
        this.connectionString = connectionString;
        readUnsent = $"""
            SELECT TOP (@Max) Sequence, OccurredAt, Type, Subject, Data, TraceParent
            FROM {schema}.Outbox WHERE SentAt IS NULL ORDER BY Sequence
            """;
        markSent = $"""
            UPDATE {schema}.Outbox SET SentAt = SYSUTCDATETIME()
            WHERE SentAt IS NULL AND Sequence IN (SELECT CAST(value AS bigint) FROM OPENJSON(@Sequences))
            """;
    }

    public async Task<IReadOnlyList<PendingEvent>> ReadUnsentAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var connection = await Sql.OpenAsync(connectionString, cancellationToken);
        await using var command = new SqlCommand(readUnsent, connection);
        command.Parameters.Add("@Max", SqlDbType.Int).Value = max;
        List<PendingEvent> pending = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pending.Add(new PendingEvent(reader.GetInt64(0),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
                new OutboxEvent(reader.GetString(2), reader.GetString(3), reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return pending;
    }

    public async Task MarkSentAsync(IReadOnlyCollection<long> sequences, CancellationToken cancellationToken = default)
    {
        if (sequences.Count == 0) return;
        await using var connection = await Sql.OpenAsync(connectionString, cancellationToken);
        await using var command = new SqlCommand(markSent, connection);
        command.Parameters.Add("@Sequences", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(sequences, OutboxJson.Default.IReadOnlyCollectionInt64);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(IReadOnlyCollection<long>))]
internal sealed partial class OutboxJson : System.Text.Json.Serialization.JsonSerializerContext;

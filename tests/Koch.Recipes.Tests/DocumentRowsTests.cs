using System.Diagnostics;
using Koch.ServiceDefaults.Documents;
using Koch.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koch.Recipes.Tests;

/// <summary>
/// What every row store and its outbox must do; run against SQL Server and against the memory store the other tests
/// use.
/// </summary>
public abstract class DocumentRowsContract
{
    protected Task<IDocumentRows> StoreAsync() => StoreAsync(null);

    /// <param name="events">The events a change causes, like the Recipes service's photo events.</param>
    protected abstract Task<IDocumentRows> StoreAsync(RowEvents? events);

    /// <summary>The outbox of the store made last.</summary>
    protected abstract IOutbox Outbox { get; }

    /// <summary>How often the row was saved (its Version column).</summary>
    protected abstract Task<int> VersionOfAsync(string id);

    /// <summary>One event per change, saying what the row was and became.</summary>
    private static IEnumerable<OutboxEvent> Echo(RowChange change) =>
        [new OutboxEvent("changed", change.Id, $"{{\"before\":{change.Before ?? "null"},\"after\":{change.After ?? "null"}}}")];

    [Fact]
    public async Task Written_rows_are_read_back_in_id_order()
    {
        var store = await StoreAsync();

        var rows = await store.WriteAndReadAsync([new("recipe:b", """{"x":2}"""), new("recipe:a", """{"x":1}""")]);

        Assert.Equal([new StoredRow("recipe:a", """{"x":1}"""), new StoredRow("recipe:b", """{"x":2}""")], rows);
    }

    [Fact]
    public async Task A_removed_row_is_no_longer_read_but_can_come_back()
    {
        var store = await StoreAsync();
        await store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]);

        Assert.Empty(await store.WriteAndReadAsync([new("recipe:a", null)]));
        Assert.Single(await store.WriteAndReadAsync([new("recipe:a", """{"x":3}""")]));
    }

    [Fact]
    public async Task Writing_the_same_json_again_makes_no_new_version()
    {
        var store = await StoreAsync();
        await store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]);
        await store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]);
        Assert.Equal(1, await VersionOfAsync("recipe:a"));

        await store.WriteAndReadAsync([new("recipe:a", """{"x":2}""")]);
        Assert.Equal(2, await VersionOfAsync("recipe:a"));
    }

    [Fact]
    public async Task A_change_of_case_only_is_a_change()
    {
        // The database's collation ignores case; "salz" corrected to "Salz" must still be saved.
        var store = await StoreAsync();
        await store.WriteAndReadAsync([new("recipe:a", """{"x":"salz"}""")]);

        var rows = await store.WriteAndReadAsync([new("recipe:a", """{"x":"Salz"}""")]);

        Assert.Equal("""{"x":"Salz"}""", Assert.Single(rows).Json);
        Assert.Equal(2, await VersionOfAsync("recipe:a"));
    }

    [Fact]
    public async Task Removing_what_was_never_there_is_harmless()
    {
        var store = await StoreAsync();

        Assert.Empty(await store.WriteAndReadAsync([new("recipe:gone", null)]));
    }

    [Fact]
    public async Task A_change_saves_its_events_with_what_was_and_is_live()
    {
        var store = await StoreAsync(Echo);

        await store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]);
        await store.WriteAndReadAsync([new("recipe:a", """{"x":2}""")]);
        await store.WriteAndReadAsync([new("recipe:a", null)]);

        var events = await Outbox.ReadUnsentAsync(10);
        Assert.Equal(
            ["""{"before":null,"after":{"x":1}}""", """{"before":{"x":1},"after":{"x":2}}""", """{"before":{"x":2},"after":null}"""],
            events.Select(e => e.Event.Data));
        Assert.All(events, e => Assert.Equal("recipe:a", e.Event.Subject));
        Assert.Equal(events.Select(e => e.Sequence).Order(), events.Select(e => e.Sequence));
    }

    [Fact]
    public async Task An_unchanged_row_saves_no_event()
    {
        var store = await StoreAsync(Echo);
        await store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:gone", null)]);

        await store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:gone", null)]);

        // Removing what was never there is no change either.
        Assert.Single(await Outbox.ReadUnsentAsync(10));
    }

    [Fact]
    public async Task When_deriving_an_event_fails_neither_rows_nor_events_are_saved()
    {
        var calls = 0;
        var store = await StoreAsync(change => ++calls == 2 ? throw new InvalidOperationException("boom") : Echo(change));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:b", """{"x":2}""")]));

        Assert.Empty(await Outbox.ReadUnsentAsync(10));
        Assert.Empty(await store.WriteAndReadAsync([]));
    }

    [Fact]
    public async Task A_merge_runs_on_the_row_as_stored_and_never_on_a_missing_or_removed_one()
    {
        var store = await StoreAsync();
        await store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:gone", "{}")]);
        await store.WriteAndReadAsync([new("recipe:gone", null)]);
        string? seen = null;

        var rows = await store.WriteAndReadAsync(
        [
            new("recipe:a", """{"x":2}""", stored => { seen = stored; return """{"x":3}"""; }),
            new("recipe:b", """{"y":1}""", _ => throw new InvalidOperationException("Nothing to merge with.")),
            new("recipe:gone", """{"z":1}""", _ => throw new InvalidOperationException("Removed: nothing to merge with.")),
        ]);

        Assert.Equal("""{"x":1}""", seen);
        Assert.Equal(["""{"x":3}""", """{"y":1}""", """{"z":1}"""], rows.Select(r => r.Json));
    }

    [Fact]
    public async Task An_event_remembers_the_trace_it_was_saved_in()
    {
        var store = await StoreAsync(Echo);
        string traceParent;
        using (var request = new Activity("sync").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            traceParent = request.Id!;
            await store.WriteAndReadAsync([new("recipe:a", "{}")]);
        }
        await store.WriteAndReadAsync([new("recipe:b", "{}")]);

        var events = await Outbox.ReadUnsentAsync(10);
        Assert.Equal([traceParent, null], events.Select(e => e.TraceParent));
    }

    [Fact]
    public async Task Events_marked_sent_are_not_read_again_and_the_others_are()
    {
        var store = await StoreAsync(Echo);
        await store.WriteAndReadAsync([new("recipe:a", "{}"), new("recipe:b", "{}"), new("recipe:c", "{}")]);
        var events = await Outbox.ReadUnsentAsync(10);

        // As if the second had committed after the relay read the others.
        await Outbox.MarkSentAsync([events[0].Sequence, events[2].Sequence]);

        Assert.Equal([events[1]], await Outbox.ReadUnsentAsync(10));
        Assert.Equal([events[1]], await Outbox.ReadUnsentAsync(1));
    }
}

public sealed class MemoryDocumentRowsTests : DocumentRowsContract
{
    private MemoryDocumentRows store = new();

    protected override IOutbox Outbox => store;

    protected override Task<IDocumentRows> StoreAsync(RowEvents? events)
    {
        // Every test starts empty; a store with events replaces the plain one.
        if (events is not null) store = new MemoryDocumentRows(events);
        return Task.FromResult<IDocumentRows>(store);
    }

    protected override Task<int> VersionOfAsync(string id) => Task.FromResult(store.VersionOf(id));
}

public sealed class SqlDocumentRowsTests : DocumentRowsContract, IAsyncLifetime
{
    private readonly SqlDatabase sql = new();

    public async Task InitializeAsync()
    {
        await sql.InitializeAsync();
        foreach (var script in SqlDatabase.Scripts("recipes")) await sql.ExecuteAsync(script);
    }

    public Task DisposeAsync() => sql.DisposeAsync();

    protected override IOutbox Outbox => new SqlOutbox(sql.ConnectionString, "recipes");

    protected override Task<IDocumentRows> StoreAsync(RowEvents? events) => Task.FromResult<IDocumentRows>(
        new SqlDocumentRows(sql.ConnectionString, "recipes", NullLogger<SqlDocumentRows>.Instance, events));

    protected override async Task<int> VersionOfAsync(string id) =>
        (int)(await sql.ExecuteAsync($"SELECT Version FROM recipes.Documents WHERE Id = '{id}'") ?? 0);

    [Fact]
    public void A_schema_name_is_never_anything_but_letters()
    {
        Assert.Throws<ArgumentException>(() =>
            new SqlDocumentRows(sql.ConnectionString, "recipes; DROP TABLE x", NullLogger<SqlDocumentRows>.Instance));
        Assert.Throws<ArgumentException>(() => new SqlOutbox(sql.ConnectionString, "recipes.Outbox--"));
    }

    [Fact]
    public async Task A_failing_request_saves_none_of_its_rows()
    {
        var store = await StoreAsync();

        // The second row is not JSON, which the table refuses: the first must not stay either.
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:b", "not json")]));

        Assert.Equal(0, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Documents"));
    }

    [Fact]
    public async Task When_the_rows_fail_after_their_events_were_saved_no_event_remains()
    {
        // The first row's event is inserted, then the second row fails: the transaction takes the event back with it,
        // so no event is ever published for a change that wasn't saved (DDIA ch12 p.507-508).
        var store = await StoreAsync(change => [new OutboxEvent("changed", change.Id, "{}")]);

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            store.WriteAndReadAsync([new("recipe:a", """{"x":1}"""), new("recipe:b", "not json")]));

        Assert.Equal(0, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Outbox"));
        Assert.Equal(0, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Documents"));
    }

    [Fact]
    public async Task When_saving_an_event_fails_its_row_is_not_saved()
    {
        var store = await StoreAsync(change => [new OutboxEvent("changed", change.Id, "not json")]);

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            store.WriteAndReadAsync([new("recipe:a", """{"x":1}""")]));

        Assert.Equal(0, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Documents"));
    }

    [Fact]
    public async Task A_sent_event_stays_with_the_time_it_was_sent()
    {
        var store = await StoreAsync(change => [new OutboxEvent("changed", change.Id, "{}")]);
        await store.WriteAndReadAsync([new("recipe:a", "{}")]);

        await Outbox.MarkSentAsync([(await Outbox.ReadUnsentAsync(1))[0].Sequence]);

        Assert.Equal(1, await sql.ExecuteAsync("SELECT COUNT(*) FROM recipes.Outbox WHERE SentAt IS NOT NULL"));
    }
}

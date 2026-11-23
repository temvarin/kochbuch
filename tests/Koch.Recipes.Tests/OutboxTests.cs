using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koch.Recipes.Events;
using Koch.Recipes.Outbox;
using Koch.Recipes.Storage;
using Koch.ServiceDefaults.Documents;
using Koch.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Koch.Recipes.Tests;

/// <summary>Which recipe changes cause photo events (docs/adr/0008).</summary>
public class PhotoEventTests
{
    private static readonly Guid Recipe = new("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80");
    private static readonly Guid Old = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid New = new("22222222-2222-4222-8222-222222222222");

    private static string? Row(Guid? photo, string title = "Suppe") => photo is { } id
        ? $$"""{"id":"{{Recipe}}","title":"{{title}}","servings":2,"lines":[],"steps":[],"notes":"","photo":"{{id}}"}"""
        : $$"""{"id":"{{Recipe}}","title":"{{title}}","servings":2,"lines":[],"steps":[],"notes":""}""";

    private static IEnumerable<(string Type, Guid Photo)> Events(string? before, string? after) =>
        Rows.PhotoEvents(new RowChange($"recipe:{Recipe}", before, after)).Select(e =>
        {
            var usage = JsonSerializer.Deserialize(e.Data, PhotoEventsJson.Default.PhotoUsageJson)!;
            Assert.Equal(Recipe, usage.Recipe);
            Assert.Equal(PhotoEvents.Subject(usage.Photo), e.Subject);
            return (e.Type, usage.Photo);
        });

    [Fact]
    public void A_new_recipe_with_a_photo_attaches_it() =>
        Assert.Equal([(PhotoEvents.Attached, New)], Events(null, Row(New)));

    [Fact]
    public void A_photo_added_later_is_attached() =>
        Assert.Equal([(PhotoEvents.Attached, New)], Events(Row(null), Row(New)));

    [Fact]
    public void Another_photo_detaches_the_old_one_and_attaches_the_new_one() =>
        Assert.Equal([(PhotoEvents.Detached, Old), (PhotoEvents.Attached, New)], Events(Row(Old), Row(New)));

    [Fact]
    public void A_removed_photo_is_detached() =>
        Assert.Equal([(PhotoEvents.Detached, Old)], Events(Row(Old), Row(null)));

    [Fact]
    public void A_deleted_recipe_detaches_its_photo() =>
        Assert.Equal([(PhotoEvents.Detached, Old)], Events(Row(Old), null));

    [Fact]
    public void Other_changes_to_a_recipe_cause_no_event()
    {
        Assert.Empty(Events(Row(Old), Row(Old, "Kartoffelsuppe")));
        Assert.Empty(Events(Row(null), Row(null, "Kartoffelsuppe")));
    }

    [Fact]
    public void Rows_that_are_not_recipes_cause_no_event() => // ingredient rows from before docs/adr/0013
        Assert.Empty(Rows.PhotoEvents(new RowChange("ingredient:x", null, """{"name":"Salz","aisle":"sonstiges"}""")));
}

/// <summary>The relay from the outbox to Service Bus, with the outbox in memory and a list for Service Bus.</summary>
public class OutboxRelayTests
{
    private sealed class Broker : IEventPublisher
    {
        public List<PendingEvent> Received { get; } = [];
        public int FailNext { get; set; }

        public Task PublishAsync(IReadOnlyList<PendingEvent> events, CancellationToken cancellationToken)
        {
            if (FailNext-- > 0) throw new InvalidOperationException("Service Bus is away.");
            Received.AddRange(events);
            return Task.CompletedTask;
        }
    }

    /// <summary>An outbox whose marking fails once: the crash between "Service Bus has it" and "marked sent".</summary>
    private sealed class ForgetfulOutbox(IOutbox inner) : IOutbox
    {
        public bool FailMark { get; set; }

        public Task<IReadOnlyList<PendingEvent>> ReadUnsentAsync(int max, CancellationToken cancellationToken = default) =>
            inner.ReadUnsentAsync(max, cancellationToken);

        public Task MarkSentAsync(IReadOnlyCollection<long> sequences, CancellationToken cancellationToken = default)
        {
            if (!FailMark) return inner.MarkSentAsync(sequences, cancellationToken);
            FailMark = false;
            throw new InvalidOperationException("The database is away.");
        }
    }

    private static async Task<MemoryDocumentRows> Saved(int recipes)
    {
        var store = new MemoryDocumentRows(change => [new OutboxEvent("t", change.Id, "{}")]);
        await store.WriteAndReadAsync([.. Enumerable.Range(0, recipes).Select(i => new RowWrite($"recipe:{i:D3}", "{}"))]);
        return store;
    }

    private static OutboxRelay Relay(IOutbox outbox, IEventPublisher broker) =>
        new(outbox, broker, new OutboxWakeUp(), new FakeTimeProvider(), NullLogger<OutboxRelay>.Instance);

    [Fact]
    public async Task Everything_in_the_outbox_is_published_in_order_and_then_not_again()
    {
        var store = await Saved(OutboxRelay.BatchSize + 3);
        var broker = new Broker();

        Assert.Equal(OutboxRelay.BatchSize + 3, await Relay(store, broker).DrainAsync(CancellationToken.None));
        Assert.Equal(0, await Relay(store, broker).DrainAsync(CancellationToken.None));

        Assert.Equal(broker.Received.Select(e => e.Sequence).Order(), broker.Received.Select(e => e.Sequence));
        Assert.Equal(OutboxRelay.BatchSize + 3, broker.Received.Count);
    }

    [Fact]
    public async Task When_Service_Bus_is_away_the_events_wait_for_the_next_round()
    {
        var store = await Saved(2);
        var broker = new Broker { FailNext = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Relay(store, broker).DrainAsync(CancellationToken.None));
        Assert.Equal(2, (await store.ReadUnsentAsync(10)).Count);

        Assert.Equal(2, await Relay(store, broker).DrainAsync(CancellationToken.None));
        Assert.Empty(await store.ReadUnsentAsync(10));
    }

    [Fact]
    public async Task An_event_whose_sending_was_not_recorded_is_sent_again()
    {
        // At least once, never lost: the consumer recognises the repeat by its sequence (DDIA ch12 p.493).
        var store = await Saved(1);
        var outbox = new ForgetfulOutbox(store) { FailMark = true };
        var broker = new Broker();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Relay(outbox, broker).DrainAsync(CancellationToken.None));
        await Relay(outbox, broker).DrainAsync(CancellationToken.None);

        Assert.Equal(2, broker.Received.Count);
        Assert.Equal(broker.Received[0], broker.Received[1]);
    }

    [Fact]
    public async Task A_ring_wakes_the_relay_before_its_interval()
    {
        var wakeUp = new OutboxWakeUp();
        var clock = new FakeTimeProvider();
        var waiting = wakeUp.WaitAsync(OutboxRelay.Interval, clock, CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        wakeUp.Ring();
        wakeUp.Ring(); // rings while it works add up to one round

        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System);
        await wakeUp.WaitAsync(OutboxRelay.Interval, clock, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System);
        var third = wakeUp.WaitAsync(OutboxRelay.Interval, clock, CancellationToken.None);
        Assert.False(third.IsCompleted);
        clock.Advance(OutboxRelay.Interval);
        await third.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System);
    }
}

/// <summary>
/// The photo events as Media receives them: this version writes exactly the newest sample, and Media's tests read every
/// sample (tests/Koch.Media.Tests). Same rules as the other contracts (tests/Koch.Recipes.Domain.Tests/Contracts/README.md).
/// </summary>
public class EventContractTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Contracts");

    public const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    /// <summary>
    /// The event every sample describes: recipe 6f1d… attached photo 3c9e…, outbox row 42, saved in trace 4bf9… (since v2).
    /// </summary>
    public static PendingEvent Example(string type, string? traceParent = TraceParent) => new(42,
        new DateTimeOffset(2026, 11, 22, 18, 30, 0, TimeSpan.Zero),
        new OutboxEvent(type, PhotoEvents.Subject(new Guid("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f")),
            PhotoEvents.Data(new PhotoUsageJson(new Guid("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f"), new Guid("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80")))),
        traceParent);

    [Fact]
    public void The_trace_goes_with_the_event_for_any_consumer_and_for_service_bus()
    {
        var message = CloudEvents.Message(Example(PhotoEvents.Attached));

        Assert.Equal(TraceParent, JsonNode.Parse(message.Body.ToString())![PhotoEvents.TraceParentAttribute]!.GetValue<string>());
        Assert.Equal(TraceParent, message.ApplicationProperties[CloudEvents.DiagnosticId]);
    }

    [Fact]
    public void An_event_saved_outside_a_trace_says_nothing_about_one()
    {
        // Service Bus then records the relay's own send, which is right: there is nothing older to point to.
        var message = CloudEvents.Message(Example(PhotoEvents.Attached, traceParent: null));

        Assert.False(JsonNode.Parse(message.Body.ToString())!.AsObject().ContainsKey(PhotoEvents.TraceParentAttribute));
        Assert.False(message.ApplicationProperties.ContainsKey(CloudEvents.DiagnosticId));
    }

    public static TheoryData<string, string> Names => new()
    {
        { "event-photo-attached", PhotoEvents.Attached },
        { "event-photo-detached", PhotoEvents.Detached },
    };

    [Theory, MemberData(nameof(Names))]
    public void This_version_publishes_exactly_the_newest_sample(string name, string type)
    {
        var message = CloudEvents.Message(Example(type));
        Assert.Equal(CloudEvents.ContentType, message.ContentType);
        Assert.Equal("42", message.MessageId);
        var written = message.Body.ToString();

        var newest = Directory.Exists(Path.Combine(Folder, name))
            ? Directory.GetFiles(Path.Combine(Folder, name), "v*.json").Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        if (newest is not null && JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(newest)), JsonNode.Parse(written))) return;

        var received = Path.Combine(SourceFolder(), name, "received.json");
        Directory.CreateDirectory(Path.GetDirectoryName(received)!);
        File.WriteAllText(received, JsonNode.Parse(written)!.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Fail($"Recipes publishes {name} differently from its newest sample. Events may only gain optional data; "
            + $"if that is what changed, add {received} as Contracts/{name}/v<n>-<yyyy-MM-dd>.json.");
    }

    private static string SourceFolder([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "Contracts");
}

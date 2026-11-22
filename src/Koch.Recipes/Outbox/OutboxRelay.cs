using System.Diagnostics;
using System.Threading.Channels;
using Azure.Messaging.ServiceBus;
using Koch.ServiceDefaults.Documents;

namespace Koch.Recipes.Outbox;

/// <summary>Where the relay hands events over; Service Bus in Azure, a list in tests.</summary>
public interface IEventPublisher
{
    /// <summary>Returns once the broker has them all; throws if it may not have.</summary>
    Task PublishAsync(IReadOnlyList<PendingEvent> events, CancellationToken cancellationToken);
}

/// <summary>The photo events into the Service Bus queue <c>photo-lifecycle</c>, with the service's managed identity.</summary>
public sealed class ServiceBusPublisher(ServiceBusSender sender) : IEventPublisher
{
    public Task PublishAsync(IReadOnlyList<PendingEvent> events, CancellationToken cancellationToken) =>
        sender.SendMessagesAsync(events.Select(CloudEvents.Message), cancellationToken);
}

/// <summary>Tells the relay that a request has just saved rows, and maybe events with them.</summary>
public sealed class OutboxWakeUp
{
    private readonly Channel<bool> rings = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Ring() => rings.Writer.TryWrite(true);

    /// <summary>Waits for a ring or the interval, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan interval, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var rung = rings.Reader.ReadAsync(timeout.Token).AsTask();
        var elapsed = Task.Delay(interval, clock, timeout.Token);
        await Task.WhenAny(rung, elapsed);
        await timeout.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>
/// Publishes what the outbox holds: right after a request saved something, at start, and every few minutes in case
/// Service Bus was away. Marks events sent only after Service Bus has them, so an event is published at least once;
/// a crash in between publishes it again, which the consumer recognises by its sequence (DDIA ch12 p.493, p.507-508;
/// docs/adr/0008).
/// <para>
/// The service scales to zero: events saved just before it stops wait for the next request. That is minutes to days for
/// clean-up that takes 30 days anyway.
/// </para>
/// </summary>
public sealed partial class OutboxRelay(IOutbox outbox, IEventPublisher publisher, OutboxWakeUp wakeUp, TimeProvider clock,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    /// <summary>Service Bus Basic takes a batch of up to 256 KB; a photo event is about 400 bytes.</summary>
    public const int BatchSize = 50;

    /// <summary>The safety net. Gentle on the database, which is a 5-DTU Basic one shared with kassensturz.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public const string ActivitySourceName = "Koch.Recipes.Outbox";

    /// <summary>
    /// One span per round, so its database reads and Service Bus send show up together rather than as loose spans
    /// every five minutes. Each message still carries the trace of the request that saved it (docs/adr/0009).
    /// </summary>
    private static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>Publishes until the outbox is empty.</summary>
    /// <returns>How many events were published.</returns>
    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        while (true)
        {
            var batch = await outbox.ReadUnsentAsync(BatchSize, cancellationToken);
            if (batch.Count == 0) return published;
            await publisher.PublishAsync(batch, cancellationToken);
            await outbox.MarkSentAsync([.. batch.Select(e => e.Sequence)], cancellationToken);
            published += batch.Count;
            if (batch.Count < BatchSize) return published;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                using (var activity = Source.StartActivity("outbox relay"))
                {
                    try
                    {
                        var published = await DrainAsync(stoppingToken);
                        activity?.SetTag("koch.outbox.published", published);
                        if (published > 0) Published(logger, published);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        // The database or Service Bus is away: the events stay in the outbox until the next round.
                        activity?.SetStatus(ActivityStatusCode.Error, e.Message);
                        RelayFailed(logger, e);
                    }
                }
                await wakeUp.WaitAsync(Interval, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {Count} events from the outbox.")]
    private static partial void Published(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing the outbox failed; trying again later.")]
    private static partial void RelayFailed(ILogger logger, Exception exception);
}

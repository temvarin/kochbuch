using Koch.Recipes.Events;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Koch.Media.Lifecycle;

/// <summary>
/// Recipes attached or detached a photo: the event comes from its outbox through the Service Bus queue
/// <c>photo-lifecycle</c>. A failure throws, so Service Bus delivers it again and finally dead-letters it.
/// </summary>
public sealed partial class ApplyPhotoEvent(PhotoLifecycle lifecycle, ILogger<ApplyPhotoEvent> logger)
{
    [Function(nameof(ApplyPhotoEvent))]
    public async Task Run([ServiceBusTrigger(PhotoEvents.Queue, Connection = "ServiceBus")] string message, CancellationToken cancellationToken)
    {
        if (PhotoEvent.TryParse(BinaryData.FromString(message)) is not { } photoEvent)
        {
            NotAPhotoEvent(logger);
            return;
        }
        using var activity = PhotoTracing.StartApply(photoEvent);
        var outcome = await lifecycle.ApplyAsync(photoEvent, cancellationToken);
        activity?.SetTag("koch.photo.outcome", outcome.ToString());
        Applied(logger, photoEvent.Photo, photoEvent.Usage.State, photoEvent.Usage.Sequence, outcome);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A message that is not a photo event was dropped.")]
    private static partial void NotAPhotoEvent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Photo {Id} {State} (sequence {Sequence}): {Outcome}.")]
    private static partial void Applied(ILogger logger, Guid id, string state, string sequence, LifecycleOutcome outcome);
}

/// <summary>Once a day, at night (UTC), deletes the photos no recipe has shown for 30 days (docs/adr/0008).</summary>
public sealed class SweepDetachedPhotos(PhotoSweeper sweeper)
{
    [Function(nameof(SweepDetachedPhotos))]
    public Task Run([TimerTrigger("0 30 3 * * *")] TimerInfo timer, CancellationToken cancellationToken) =>
        sweeper.SweepAsync(cancellationToken);
}

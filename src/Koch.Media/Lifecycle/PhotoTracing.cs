using System.Diagnostics;
using Koch.Recipes.Events;

namespace Koch.Media.Lifecycle;

/// <summary>
/// Puts Media's work on a photo event into the trace of the sync request that caused it, so one trace shows the phone's
/// sync, the gateway, Recipes, the outbox and Media (docs/adr/0009). The event says where that trace is
/// (<see cref="PhotoEvents.TraceParentAttribute"/>); the function's own invocation, a trace of its own, becomes a link.
/// </summary>
public static class PhotoTracing
{
    public const string SourceName = "Koch.Media";

    private static readonly ActivitySource Source = new(SourceName);

    /// <returns>The span to dispose when done; null when no one listens (locally, in tests without a listener).</returns>
    public static Activity? StartApply(PhotoEvent photoEvent)
    {
        var invocation = Activity.Current?.Context ?? default;
        Activity? activity;
        if (photoEvent.TraceParent is { } traceParent && ActivityContext.TryParse(traceParent, null, isRemote: true, out var origin)
            && origin.TraceId != invocation.TraceId)
        {
            ActivityLink[] links = invocation == default ? [] : [new ActivityLink(invocation)];
            activity = Source.StartActivity("apply photo event", ActivityKind.Consumer, origin, links: links);
        }
        else
        {
            activity = Source.StartActivity("apply photo event", ActivityKind.Consumer);
        }
        return activity?
            .SetTag("koch.photo.id", photoEvent.Photo.ToString("D"))
            .SetTag("koch.photo.state", photoEvent.Usage.State)
            .SetTag("koch.event.sequence", photoEvent.Usage.Sequence);
    }
}

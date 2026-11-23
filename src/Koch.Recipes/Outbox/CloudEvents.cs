using System.Globalization;
using Azure.Messaging;
using Azure.Messaging.ServiceBus;
using Koch.Recipes.Events;
using Koch.ServiceDefaults.Documents;

namespace Koch.Recipes.Outbox;

/// <summary>
/// An outbox row as the message Media receives: a CloudEvent in structured JSON, so the envelope (type, source, id,
/// time, sequence) is a published standard and only the data is ours (Koch.Recipes.Events; samples in
/// tests/Koch.Recipes.Tests/Contracts/event-photo-*).
/// </summary>
public static class CloudEvents
{
    public const string ContentType = "application/cloudevents+json";

    /// <summary>The application property Service Bus clients read a message's trace context from.</summary>
    public const string DiagnosticId = "Diagnostic-Id";

    public static CloudEvent For(PendingEvent pending)
    {
        var id = pending.Sequence.ToString(CultureInfo.InvariantCulture);
        var cloudEvent = new CloudEvent(PhotoEvents.Source, pending.Event.Type, BinaryData.FromString(pending.Event.Data),
            "application/json", CloudEventDataFormat.Json)
        {
            // The outbox row's number: unique, so a consumer can tell a repeat from a new event.
            Id = id,
            Time = pending.OccurredAt,
            Subject = pending.Event.Subject,
        };
        cloudEvent.ExtensionAttributes[PhotoEvents.SequenceAttribute] = PhotoEvents.Sequence(pending.Sequence);
        if (pending.TraceParent is { } traceParent) cloudEvent.ExtensionAttributes[PhotoEvents.TraceParentAttribute] = traceParent;
        return cloudEvent;
    }

    public static ServiceBusMessage Message(PendingEvent pending)
    {
        var cloudEvent = For(pending);
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(cloudEvent))
        {
            ContentType = ContentType,
            MessageId = cloudEvent.Id,
            Subject = cloudEvent.Subject,
        };
        // Service Bus' own trace property. The SDK fills it from the sender's current span unless it is set; set, it
        // points back at the request that saved the event, not at the relay that happens to send it.
        if (pending.TraceParent is { } traceParent) message.ApplicationProperties[DiagnosticId] = traceParent;
        return message;
    }
}

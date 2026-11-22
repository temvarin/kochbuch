using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Koch.Media.Uploads;

/// <summary>
/// A photo was uploaded: Event Grid saw BlobCreated in <c>uploads/</c> and put it in the Service Bus queue
/// <c>photo-uploads</c>. A failure (storage briefly away) throws, so Service Bus delivers it again and, after its
/// delivery count, moves it to the dead-letter queue, which an alert watches (docs/adr/0006).
/// </summary>
public sealed partial class ProcessUpload(PhotoProcessor processor, ILogger<ProcessUpload> logger)
{
    public const string Queue = "photo-uploads";

    [Function(nameof(ProcessUpload))]
    public async Task Run([ServiceBusTrigger(Queue, Connection = "ServiceBus")] string message, CancellationToken cancellationToken)
    {
        if (!BlobCreated.TryParse(message, out var id))
        {
            // Nothing that retrying could fix: logged and completed.
            NotAnUpload(logger);
            return;
        }
        var outcome = await processor.ProcessAsync(id, cancellationToken);
        Processed(logger, id, outcome);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A message that is not a BlobCreated of an upload was dropped.")]
    private static partial void NotAnUpload(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Photo {Id}: {Outcome}.")]
    private static partial void Processed(ILogger logger, Guid id, Outcome outcome);
}

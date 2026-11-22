using System.Diagnostics;
using System.Text.Json.Nodes;
using Koch.Media.Contracts;
using Koch.Media.Lifecycle;
using Koch.Media.Uploads;
using Koch.Recipes.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Koch.Media.Tests;

/// <summary>Photo events from Recipes: in any order, any number of times (docs/adr/0008).</summary>
public class PhotoLifecycleTests
{
    private static readonly Guid Id = new("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f");
    private static readonly DateOnly Today = new(2026, 11, 22);
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));

    private static PhotoProcessor Processor(MemoryBlobs blobs) => new(blobs, Clock, NullLogger<PhotoProcessor>.Instance);

    private static PhotoLifecycle Lifecycle(MemoryBlobs blobs) => new(blobs, Processor(blobs), NullLogger<PhotoLifecycle>.Instance);

    private static PhotoEvent Event(string state, long sequence, DateOnly? day = null) =>
        new(Id, new PhotoUsage(state, PhotoEvents.Sequence(sequence), day ?? Today));

    private static async Task<MemoryBlobs> Processed()
    {
        var blobs = new MemoryBlobs();
        blobs.Uploads[Id] = Photos.Jpeg(1600, 1200);
        await Processor(blobs).ProcessAsync(Id, CancellationToken.None);
        return blobs;
    }

    private static void AssertEveryRendition(MemoryBlobs blobs, string state, long sequence)
    {
        foreach (var width in PhotoPaths.Widths)
        {
            Assert.Equal(state, blobs.UsageOf(Id, width).State);
            Assert.Equal(PhotoEvents.Sequence(sequence), blobs.UsageOf(Id, width).Sequence);
        }
    }

    [Fact]
    public async Task New_renditions_are_tagged_new_with_sequence_zero()
    {
        var blobs = await Processed();

        Assert.All(PhotoPaths.Widths, width => Assert.Equal(PhotoUsage.Fresh(Today), blobs.UsageOf(Id, width)));
    }

    [Fact]
    public async Task An_event_sets_the_state_of_every_rendition()
    {
        var blobs = await Processed();

        Assert.Equal(LifecycleOutcome.Applied, await Lifecycle(blobs).ApplyAsync(Event(PhotoUsage.Attached, 5), CancellationToken.None));

        AssertEveryRendition(blobs, PhotoUsage.Attached, 5);
    }

    [Fact]
    public async Task An_older_event_that_comes_late_changes_nothing()
    {
        // Detached (6) is applied first; the attach before it (5) was redelivered after a failure.
        var blobs = await Processed();
        await Lifecycle(blobs).ApplyAsync(Event(PhotoUsage.Detached, 6), CancellationToken.None);

        Assert.Equal(LifecycleOutcome.Stale, await Lifecycle(blobs).ApplyAsync(Event(PhotoUsage.Attached, 5), CancellationToken.None));

        AssertEveryRendition(blobs, PhotoUsage.Detached, 6);
    }

    [Fact]
    public async Task The_same_event_twice_is_applied_once()
    {
        var blobs = await Processed();
        await Lifecycle(blobs).ApplyAsync(Event(PhotoUsage.Detached, 6), CancellationToken.None);

        Assert.Equal(LifecycleOutcome.Stale, await Lifecycle(blobs).ApplyAsync(Event(PhotoUsage.Detached, 6), CancellationToken.None));
    }

    [Fact]
    public async Task An_event_before_the_upload_was_processed_processes_it_and_the_upload_message_keeps_the_state()
    {
        var blobs = new MemoryBlobs();
        blobs.Uploads[Id] = Photos.Jpeg(1600, 1200);

        Assert.Equal(LifecycleOutcome.Applied, await Lifecycle(blobs).ApplyAsync(Event(PhotoUsage.Attached, 5), CancellationToken.None));
        AssertEveryRendition(blobs, PhotoUsage.Attached, 5);
        Assert.Empty(blobs.Uploads);

        // The upload's own BlobCreated comes afterwards: already done, and the state stays.
        Assert.Equal(Outcome.AlreadyDone, await Processor(blobs).ProcessAsync(Id, CancellationToken.None));
        AssertEveryRendition(blobs, PhotoUsage.Attached, 5);
    }

    [Fact]
    public async Task Processing_the_upload_again_never_resets_the_state()
    {
        // The upload message is processing while the event comes: its renditions are created only if missing.
        var blobs = new MemoryBlobs();
        blobs.Uploads[Id] = Photos.Jpeg(1600, 1200);
        await Lifecycle(blobs).ApplyAsync(Event(PhotoUsage.Attached, 5), CancellationToken.None);
        blobs.Uploads[Id] = Photos.Jpeg(1600, 1200); // as if the upload's delete had not happened yet

        Assert.Equal(Outcome.Done, await Processor(blobs).ProcessAsync(Id, CancellationToken.None));

        AssertEveryRendition(blobs, PhotoUsage.Attached, 5);
    }

    [Fact]
    public async Task An_event_for_a_photo_that_does_not_exist_is_done()
    {
        Assert.Equal(LifecycleOutcome.NoPhoto,
            await Lifecycle(new MemoryBlobs()).ApplyAsync(Event(PhotoUsage.Detached, 6), CancellationToken.None));
    }
}

public class PhotoSweeperTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 22, 3, 30, 0, TimeSpan.Zero));

    private static readonly DateOnly Today = new(2026, 11, 22);

    private static PhotoSweeper Sweeper(MemoryBlobs blobs) => new(blobs, Clock, NullLogger<PhotoSweeper>.Instance);

    private static Guid Photo(MemoryBlobs blobs, string state, DateOnly since, long sequence = 7)
    {
        var id = Guid.NewGuid();
        foreach (var width in PhotoPaths.Widths)
            blobs.Renditions[PhotoPaths.Rendition(id, width)] = ([], new PhotoUsage(state, PhotoEvents.Sequence(sequence), since));
        return id;
    }

    private static bool Exists(MemoryBlobs blobs, Guid id) => blobs.Renditions.ContainsKey(PhotoPaths.Rendition(id, 400));

    [Fact]
    public async Task Only_photos_detached_for_more_than_thirty_days_are_deleted()
    {
        var blobs = new MemoryBlobs();
        var old = Photo(blobs, PhotoUsage.Detached, Today.AddDays(-31));
        var recent = Photo(blobs, PhotoUsage.Detached, Today.AddDays(-30));
        var attached = Photo(blobs, PhotoUsage.Attached, Today.AddDays(-400));
        var neverAttached = Photo(blobs, PhotoUsage.New, Today.AddDays(-400), 0);

        Assert.Equal(PhotoPaths.Widths.Count, await Sweeper(blobs).SweepAsync(CancellationToken.None));

        Assert.False(Exists(blobs, old));
        Assert.True(Exists(blobs, recent));
        Assert.True(Exists(blobs, attached));
        Assert.True(Exists(blobs, neverAttached));
    }

    [Fact]
    public async Task A_photo_attached_again_during_the_sweep_stays()
    {
        var blobs = new MemoryBlobs();
        var id = Photo(blobs, PhotoUsage.Detached, Today.AddDays(-40));
        // Found as detached, then a late "attached" arrives before the delete.
        blobs.BeforeDelete = name => blobs.Renditions[name] = (blobs.Renditions[name].Webp,
            new PhotoUsage(PhotoUsage.Attached, PhotoEvents.Sequence(8), Today));

        Assert.Equal(0, await Sweeper(blobs).SweepAsync(CancellationToken.None));
        Assert.True(Exists(blobs, id));
    }
}

/// <summary>The tags and conditions Blob Storage evaluates; a typo here would match nothing, silently.</summary>
public class PhotoUsageTests
{
    [Fact]
    public void Tags_are_the_state_the_padded_sequence_and_the_day()
    {
        var usage = new PhotoUsage(PhotoUsage.Detached, PhotoEvents.Sequence(42), new DateOnly(2026, 11, 22));

        Assert.Equal(new Dictionary<string, string>
        {
            ["state"] = "detached",
            ["seq"] = "00000000000000000042",
            ["since"] = "2026-11-22",
        }, usage.ToTags());
        Assert.Equal("\"seq\" < '00000000000000000042'", usage.IfNewer);
        Assert.Equal("\"state\" = 'detached' AND \"since\" < '2026-10-23'", PhotoUsage.DetachedBefore(new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public void Padded_sequences_compare_as_strings_the_way_they_do_as_numbers()
    {
        Assert.True(string.CompareOrdinal(PhotoEvents.Sequence(9), PhotoEvents.Sequence(10)) < 0);
        Assert.True(string.CompareOrdinal(PhotoEvents.Sequence(0), PhotoEvents.Sequence(1)) < 0);
        Assert.True(string.CompareOrdinal(PhotoEvents.Sequence(long.MaxValue - 1), PhotoEvents.Sequence(long.MaxValue)) < 0);
    }
}

/// <summary>Media reads every photo event Recipes ever published (the samples in tests/Koch.Recipes.Tests/Contracts).</summary>
public class PhotoEventTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Contracts");

    public static TheoryData<string, string> Samples()
    {
        var samples = new TheoryData<string, string>();
        foreach (var (folder, state) in new[] { ("event-photo-attached", PhotoUsage.Attached), ("event-photo-detached", PhotoUsage.Detached) })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(Folder, folder), "v*.json"))
                samples.Add(Path.GetRelativePath(Folder, file), state);
        }
        return samples;
    }

    [Theory, MemberData(nameof(Samples))]
    public void Every_published_sample_is_read(string sample, string state)
    {
        var json = File.ReadAllText(Path.Combine(Folder, sample));
        var photoEvent = PhotoEvent.TryParse(BinaryData.FromString(json));

        Assert.NotNull(photoEvent);
        Assert.Equal(new Guid("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f"), photoEvent.Photo);
        Assert.Equal(new PhotoUsage(state, "00000000000000000042", new DateOnly(2026, 11, 22)), photoEvent.Usage);
        // Since v2; older samples have none, and are read all the same.
        Assert.Equal(JsonNode.Parse(json)![PhotoEvents.TraceParentAttribute]?.GetValue<string>(), photoEvent.TraceParent);
    }

    [Theory]
    [InlineData("not a trace")]
    [InlineData("00-00000000000000000000000000000000-0000000000000000-01")]
    public void A_trace_that_is_not_valid_is_ignored_and_the_event_applied(string traceParent)
    {
        var message = $$$"""{"specversion":"1.0","id":"1","source":"/x","type":"koch.recipes.photo.attached","time":"2026-11-22T00:00:00Z","sequence":"00000000000000000001","traceparent":"{{{traceParent}}}","data":{"photo":"3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f","recipe":"6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"}}""";

        var photoEvent = PhotoEvent.TryParse(BinaryData.FromString(message));

        Assert.NotNull(photoEvent);
        Assert.Null(photoEvent.TraceParent);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"specversion":"1.0","id":"1","source":"/x","type":"koch.recipes.photo.renamed","time":"2026-11-22T00:00:00Z","sequence":"00000000000000000001","data":{"photo":"3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f","recipe":"6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"}}""")]
    [InlineData("""{"specversion":"1.0","id":"1","source":"/x","type":"koch.recipes.photo.attached","time":"2026-11-22T00:00:00Z","data":{"photo":"3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f","recipe":"6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"}}""")]
    [InlineData("""{"specversion":"1.0","id":"1","source":"/x","type":"koch.recipes.photo.attached","time":"2026-11-22T00:00:00Z","sequence":"1","data":{"photo":"3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f","recipe":"6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"}}""")]
    [InlineData("""{"specversion":"1.0","id":"1","source":"/x","type":"koch.recipes.photo.attached","time":"2026-11-22T00:00:00Z","sequence":"00000000000000000001","data":{"recipe":"6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80"}}""")]
    public void Anything_else_is_not_a_photo_event(string message)
    {
        Assert.Null(PhotoEvent.TryParse(BinaryData.FromString(message)));
    }
}

/// <summary>Media's work joins the trace of the sync request that caused the event (docs/adr/0009).</summary>
public sealed class PhotoTracingTests : IDisposable
{
    private const string Origin = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name == PhotoTracing.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public PhotoTracingTests() => ActivitySource.AddActivityListener(listener);

    public void Dispose() => listener.Dispose();

    private static PhotoEvent Event(string? traceParent) => new(new Guid("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f"),
        new PhotoUsage(PhotoUsage.Attached, PhotoEvents.Sequence(42), new DateOnly(2026, 11, 22)), traceParent);

    [Fact]
    public void The_span_continues_the_request_s_trace_and_links_the_function_invocation()
    {
        using var invocation = new Activity("invocation").SetIdFormat(ActivityIdFormat.W3C).Start();

        using var span = PhotoTracing.StartApply(Event(Origin));

        Assert.NotNull(span);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", span.TraceId.ToHexString());
        Assert.Equal("00f067aa0ba902b7", span.ParentSpanId.ToHexString());
        Assert.Equal(invocation.Context, Assert.Single(span.Links).Context);
        Assert.Equal(ActivityKind.Consumer, span.Kind);
        Assert.Equal(PhotoEvents.Sequence(42), span.GetTagItem("koch.event.sequence"));
    }

    [Fact]
    public void Without_a_trace_in_the_event_the_span_belongs_to_the_invocation()
    {
        using var invocation = new Activity("invocation").SetIdFormat(ActivityIdFormat.W3C).Start();

        using var span = PhotoTracing.StartApply(Event(null));

        Assert.NotNull(span);
        Assert.Equal(invocation.TraceId, span.TraceId);
        Assert.Equal(invocation.SpanId, span.ParentSpanId);
        Assert.Empty(span.Links);
    }
}

using Koch.App.Recipes;
using Koch.App.Shopping;
using Koch.App.Sync;
using Koch.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Koch.App.Tests;

/// <summary>localStorage in memory; a second phone is a second instance.</summary>
public sealed class MemoryStore : IKeyValueStore
{
    private readonly Dictionary<string, string> values = [];

    public string? Read(string key) => values.GetValueOrDefault(key);

    public void Write(string key, string value) => values[key] = value;
}

/// <summary>The app's real services on a phone: storage in memory, a clock the test controls, and <see cref="AppServer"/>.</summary>
public abstract class AppTestContext : BunitContext
{
    protected AppTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAppServices();
        Services.AddSingleton<IKeyValueStore>(Storage);
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddSingleton(new HttpClient(Server.Network) { BaseAddress = new Uri("http://localhost/") });
    }

    protected AppServer Server { get; } = new();

    protected MemoryStore Storage { get; } = new();

    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.Zero));

    protected SyncOverview Sync => Services.GetRequiredService<SyncOverview>();

    protected ShoppingService Shopping => Services.GetRequiredService<Koch.App.Shopping.ShoppingService>();

    protected RecipeService Recipes => Services.GetRequiredService<RecipeService>();

    /// <summary>Lets the time pass that a sync waits after a change, and the sync run.</summary>
    protected void AfterSave() => Clock.Advance(SyncTiming.AfterSave);

    protected override void Dispose(bool disposing)
    {
        if (disposing) Server.Dispose();
        base.Dispose(disposing);
    }
}

using Koch.App;
using Koch.App.Shell;
using Koch.Kernel;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after"); // the tab title set by each page's TopBar
builder.Services.AddAppServices();
builder.Services.AddSingleton<IKeyValueStore, LocalStorage>();
#pragma warning disable RS0030 // The composition root: the one place that picks the real clock.
builder.Services.AddSingleton(TimeProvider.System);
#pragma warning restore RS0030
// The gateway's /api: same origin, so the Easy Auth cookie goes along and no CORS is needed.
builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();

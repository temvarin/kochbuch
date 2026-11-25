using Koch.App;
using Koch.App.Shell;
using Koch.Domain.Kernel;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after"); // the tab title set by each page's TopBar
builder.Services.AddAppServices();
builder.Services.AddSingleton<IKeyValueStore, LocalStorage>();
builder.Services.AddSingleton(TimeProvider.System);
// The gateway's /api: same origin, so the Easy Auth cookie goes along and no CORS is needed.
builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();

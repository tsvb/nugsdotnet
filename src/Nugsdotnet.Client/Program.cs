using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nugsdotnet.Client.Services;
using Nugsdotnet.UI;
using Nugsdotnet.UI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient pointing back at our own host (the Server project serves us).
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
// Web-head implementations of the UI's gateway + media-URL abstractions.
builder.Services.AddScoped<INugsGateway, NugsApi>();
builder.Services.AddScoped<IMediaUrls, WebMediaUrls>();
// Scoped, not Singleton, because they depend on the scoped HttpClient.
// In Blazor WASM there's one root scope per app, so they're effectively
// singletons for the lifetime of the page.
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<CatalogCache>();
builder.Services.AddScoped<DashboardState>();

await builder.Build().RunAsync();

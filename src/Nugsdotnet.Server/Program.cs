using Nugsdotnet.Core;
using Nugsdotnet.Core.Api;

var builder = WebApplication.CreateBuilder(args);

// NugsClient + TokenStore + StreamInspector — shared with the MAUI head via
// Nugsdotnet.Core. tokens.json stays next to the server, as before.
builder.Services.AddNugsCore(
    Path.Combine(builder.Environment.ContentRootPath, "tokens.json"));

var app = builder.Build();

// Serve the Blazor WASM client.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapApi();

// Anything not matched by /api/* falls through to the Blazor SPA.
app.MapFallbackToFile("index.html");

app.Run();

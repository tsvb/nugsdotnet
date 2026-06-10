using System.Text.Json.Nodes;
using Nugsdotnet.Shared;

namespace Nugsdotnet.UI.Services;

/// <summary>
/// Head-agnostic data access for the UI. The web head implements this over
/// HTTP against the local /api endpoints (the browser can't talk to nugs
/// directly); the native head implements it with direct in-process
/// NugsClient calls (no CORS in a native shell). Catalog methods return raw
/// JsonNode because nugs response shapes are inconsistent — components dig
/// fields out defensively via NugsShape.
/// </summary>
public interface INugsGateway
{
    Task<SessionInfo> GetSessionAsync();
    Task LoginAsync(string? email = null, string? password = null);
    Task LogoutAsync();
    Task<JsonNode?> SearchAsync(string q);
    Task<JsonNode?> GetAlbumAsync(string id);
    Task<JsonNode?> GetArtistShowsAsync(string id);
    Task<JsonNode?> GetAllArtistsAsync();
    Task<StreamInfoResponse?> GetStreamInfoAsync(string trackId);
}

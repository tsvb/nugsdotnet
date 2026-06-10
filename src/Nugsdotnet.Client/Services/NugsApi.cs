using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Nugsdotnet.Shared;
using Nugsdotnet.UI.Services;

namespace Nugsdotnet.Client.Services;

/// <summary>
/// Web-head implementation of <see cref="INugsGateway"/> — a tiny HTTP client
/// for the local backend's /api endpoints. The browser can't talk to nugs
/// directly (CORS + locked-down audio headers), so the Server proxies.
/// Catalog calls return JsonNode because the underlying nugs response shapes
/// are inconsistent — the UI digs out fields defensively.
/// </summary>
public sealed class NugsApi : INugsGateway
{
    private readonly HttpClient _http;

    public NugsApi(HttpClient http) => _http = http;

    public async Task<SessionInfo> GetSessionAsync() =>
        await _http.GetFromJsonAsync<SessionInfo>("api/session")
        ?? new SessionInfo(false);

    public async Task LoginAsync(string? email = null, string? password = null)
    {
        var res = await _http.PostAsJsonAsync(
            "api/login", new LoginRequest(email, password));
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadFromJsonAsync<ErrorResponse>();
            throw new InvalidOperationException(err?.Error ?? $"HTTP {(int)res.StatusCode}");
        }
    }

    public Task LogoutAsync() => _http.PostAsync("api/logout", null);

    public Task<JsonNode?> SearchAsync(string q) =>
        _http.GetFromJsonAsync<JsonNode>(
            $"api/search?q={Uri.EscapeDataString(q)}");

    public Task<JsonNode?> GetAlbumAsync(string id) =>
        _http.GetFromJsonAsync<JsonNode>($"api/album/{id}");

    public Task<JsonNode?> GetArtistShowsAsync(string id) =>
        _http.GetFromJsonAsync<JsonNode>($"api/artist/{id}/shows");

    public Task<JsonNode?> GetAllArtistsAsync() =>
        _http.GetFromJsonAsync<JsonNode>("api/artists");

    /// <summary>
    /// Resolved-stream metadata for the dashboard. Throws on 404 (no stream) /
    /// 401 — callers treat a throw as "unavailable".
    /// </summary>
    public Task<StreamInfoResponse?> GetStreamInfoAsync(string trackId) =>
        _http.GetFromJsonAsync<StreamInfoResponse>($"api/stream-info/{trackId}");
}

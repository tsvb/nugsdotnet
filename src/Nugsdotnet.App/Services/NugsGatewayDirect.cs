using System.Text.Json.Nodes;
using Nugsdotnet.Core.Nugs;
using Nugsdotnet.Shared;
using Nugsdotnet.UI.Services;

namespace Nugsdotnet.App.Services;

/// <summary>
/// Native-head implementation of <see cref="INugsGateway"/>: calls NugsClient
/// directly in-process — no HTTP hop, no CORS. Mirrors the semantics of the
/// web head's /api endpoint handlers so components behave identically.
/// </summary>
public sealed class NugsGatewayDirect : INugsGateway
{
    private readonly NugsClient _nugs;
    private readonly StreamInspector _inspector;

    public NugsGatewayDirect(NugsClient nugs, StreamInspector inspector)
    {
        _nugs = nugs;
        _inspector = inspector;
    }

    public async Task<SessionInfo> GetSessionAsync()
    {
        try
        {
            var s = await _nugs.GetSessionAsync();
            return new SessionInfo(true, s.UserId, s.PlanDescription, s.IsAccessible);
        }
        catch
        {
            return new SessionInfo(false);
        }
    }

    public async Task LoginAsync(string? email = null, string? password = null)
    {
        // Mirrors the /api/login fallback chain so Login.razor's "use
        // credentials from appsettings/env" checkbox works unchanged.
        var e = email ?? Environment.GetEnvironmentVariable("NUGS_EMAIL");
        var p = password ?? Environment.GetEnvironmentVariable("NUGS_PASSWORD");
        if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p))
        {
            throw new InvalidOperationException("missing credentials");
        }
        await _nugs.LoginAsync(e, p);
    }

    public Task LogoutAsync() => _nugs.LogoutAsync();

    public Task<JsonNode?> SearchAsync(string q) => _nugs.SearchAsync(q);

    public Task<JsonNode?> GetAlbumAsync(string id) => _nugs.GetAlbumAsync(id);

    public Task<JsonNode?> GetArtistShowsAsync(string id) =>
        _nugs.GetArtistShowsAsync(id, 1, 100);

    public Task<JsonNode?> GetAllArtistsAsync() => _nugs.GetAllArtistsAsync();

    public async Task<StreamInfoResponse?> GetStreamInfoAsync(string trackId)
    {
        Session session;
        try { session = await _nugs.GetSessionAsync(); }
        catch { return null; } // not logged in — dashboard shows blank specs
        return await _inspector.GetStreamInfoAsync(
            trackId, session, _nugs, CancellationToken.None);
    }
}

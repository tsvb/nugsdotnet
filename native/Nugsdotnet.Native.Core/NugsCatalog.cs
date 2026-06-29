using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// Catalog reads (search / container / artists / artist shows). Returns raw
/// JsonNode because nugs response shapes are inconsistent across endpoints; the
/// UI digs fields out defensively via <see cref="NugsShape"/>.
/// </summary>
public sealed class NugsCatalog
{
    private readonly HttpClient _http;
    private readonly NugsAuth _auth;

    public NugsCatalog(HttpClient http, NugsAuth auth)
    {
        _http = http;
        _auth = auth;
    }

    public Task<JsonNode?> SearchAsync(string query, CancellationToken ct = default)
        => GetAsync(new()
        {
            ["method"] = "catalog.search",
            ["searchStr"] = query,
        }, ct);

    public Task<JsonNode?> GetAlbumAsync(string containerId, CancellationToken ct = default)
        => GetAsync(new()
        {
            ["method"] = "catalog.container",
            ["containerID"] = containerId,
            ["vdisp"] = "1",
        }, ct);

    public Task<JsonNode?> GetAllArtistsAsync(CancellationToken ct = default)
        => GetAsync(new()
        {
            ["method"] = "catalog.artists",
        }, ct);

    public Task<JsonNode?> GetArtistShowsAsync(
        string artistId, int offset = 1, int limit = 100, CancellationToken ct = default)
        => GetAsync(new()
        {
            ["method"] = "catalog.containersAll",
            ["artistList"] = artistId,
            ["startOffset"] = offset.ToString(CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["availType"] = "1",
            ["vdisp"] = "1",
        }, ct);

    private async Task<JsonNode?> GetAsync(Dictionary<string, string> query, CancellationToken ct)
    {
        var session = await _auth.GetSessionAsync(ct);
        var url = $"{NugsConstants.StreamApiBase}/api.aspx?{Query.ToQueryString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        NugsAuth.SetUA(req, NugsConstants.MobileUserAgent);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
    }

    /// <summary>
    /// Defensive parse of catalog.search into playable track entries. nugs nests
    /// tracks under containers; field names vary, so candidate keys are tried in
    /// order. Containers that expose no track list are skipped.
    /// </summary>
    public static List<TrackEntry> ParseSearchTracks(JsonNode? raw)
    {
        var r = NugsShape.Unwrap(raw);
        var containers = NugsShape.Arr(r,
            "containers", "Containers", "results", "Results", "items", "Items")
            ?? new JsonArray();

        var list = new List<TrackEntry>();
        foreach (var c in containers)
        {
            var show = NugsShape.Str(c, "containerInfo", "ContainerInfo", "title", "Title");
            var artist = NugsShape.Str(c, "artistName", "ArtistName", "artist", "Artist");
            var tracks = NugsShape.Arr(c, "tracks", "Tracks", "trackList", "TrackList");
            if (tracks is null) continue;
            foreach (var t in tracks)
            {
                var id = NugsShape.Str(t, "trackID", "TrackID", "trackId", "id");
                if (string.IsNullOrEmpty(id)) continue;
                var title = NugsShape.Str(t, "songTitle", "SongTitle", "trackTitle", "title", "Title");
                list.Add(new TrackEntry(id!, title, artist, show));
            }
        }
        return list;
    }

    /// <summary>Defensive parse of catalog.artists into a sorted artist list.</summary>
    public static List<ArtistEntry> ParseArtists(JsonNode? raw)
    {
        var r = NugsShape.Unwrap(raw);
        var arr = NugsShape.Arr(r,
            "artists", "Artists", "catalogArtists", "CatalogArtists", "items", "Items")
            ?? new JsonArray();

        var list = new List<ArtistEntry>(arr.Count);
        foreach (var item in arr)
        {
            var id = NugsShape.Str(item, "artistID", "ArtistID", "artistId", "id");
            var name = NugsShape.Str(item, "artistName", "ArtistName", "name");
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                list.Add(new ArtistEntry(id!, name!));
        }
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }
}

/// <summary>Tiny query-string helper shared by the catalog + stream-resolver.</summary>
internal static class Query
{
    public static string ToQueryString(Dictionary<string, string> q) =>
        string.Join("&", q.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}

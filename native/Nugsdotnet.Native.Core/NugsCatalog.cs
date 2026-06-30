using System.Globalization;
using System.Net.Http.Headers;
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
        // nugs's legacy *.aspx handlers don't reliably send an application/json
        // Content-Type, which ReadFromJsonAsync would reject — parse the body directly.
        var body = await res.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(body);
    }

    /// <summary>
    /// Parses catalog.search. nugs nests results as
    /// catalogSearchTypeContainers[] -> catalogSearchContainers[] ->
    /// { scHeader, catalogSearchResultItems[] }. Each item may carry an artist
    /// and/or a container (show or studio release). We collect the deduped
    /// artists and the container hits per labelled section.
    /// </summary>
    public static SearchView ParseSearch(JsonNode? raw)
    {
        var r = NugsShape.Unwrap(raw);
        var typeContainers = NugsShape.Arr(r, "catalogSearchTypeContainers") ?? new JsonArray();
        var artists = new List<ArtistEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sections = new List<SearchSection>();

        foreach (var tc in typeContainers)
        {
            var containers = NugsShape.Arr(tc, "catalogSearchContainers") ?? new JsonArray();
            foreach (var s in containers)
            {
                var header = NugsShape.Str(s, "scHeader");
                var items = NugsShape.Arr(s, "catalogSearchResultItems") ?? new JsonArray();
                if (items.Count == 0) continue;

                var entries = new List<ContainerEntry>();
                foreach (var item in items)
                {
                    var aid = NugsShape.Str(item, "artistID");
                    var aname = NugsShape.Str(item, "artistName");
                    if (!string.IsNullOrEmpty(aid) && !string.IsNullOrEmpty(aname) && seen.Add(aid!))
                        artists.Add(new ArtistEntry(aid!, aname!));

                    var containerId = NugsShape.Str(item, "containerID");
                    if (string.IsNullOrEmpty(containerId) || containerId == "0") continue;
                    var name = NugsShape.Str(item, "containerName") ?? "(untitled)";
                    var date = NugsShape.Str(item, "performanceDate");
                    var venue = NugsShape.Str(item, "venue");
                    entries.Add(new ContainerEntry(
                        containerId!, name, aname, date, venue, null, !string.IsNullOrEmpty(date)));
                }
                if (entries.Count > 0) sections.Add(new SearchSection(header, entries));
            }
        }
        return new SearchView(artists, sections);
    }

    /// <summary>
    /// Parses catalog.containersAll for an artist into studio releases (no
    /// performanceDate) and live shows (dated, newest first).
    /// </summary>
    public static ArtistShows ParseArtistShows(JsonNode? raw)
    {
        var r = NugsShape.Unwrap(raw);
        var containers = NugsShape.Arr(r, "containers", "Containers") ?? new JsonArray();
        string? artistName = null;
        var releases = new List<ContainerEntry>();
        var shows = new List<ContainerEntry>();

        foreach (var c in containers)
        {
            artistName ??= NugsShape.Str(c, "artistName", "ArtistName");
            var id = NugsShape.Str(c, "containerID", "ContainerID", "id");
            if (string.IsNullOrEmpty(id)) continue;
            var title = NugsShape.Str(c, "containerInfo", "ContainerInfo", "title");
            var date = NugsShape.Str(c, "performanceDate");
            var venue = NugsShape.Str(c, "venue");
            var img = NugsShape.Str(c?["img"], "url");
            var entry = new ContainerEntry(id!, title, artistName, date, venue, img, !string.IsNullOrEmpty(date));
            (string.IsNullOrEmpty(date) ? releases : shows).Add(entry);
        }
        shows.Sort((a, b) => CompareDateDesc(a.Date, b.Date));
        return new ArtistShows(artistName, releases, shows);
    }

    /// <summary>Parses catalog.container into an album/show with its track list.</summary>
    public static AlbumView ParseAlbum(JsonNode? raw)
    {
        var r = NugsShape.Unwrap(raw);
        var id = NugsShape.Str(r, "containerID", "ContainerID", "id") ?? "";
        var title = NugsShape.Str(r, "containerInfo", "ContainerInfo", "title");
        var artist = NugsShape.Str(r, "artistName", "ArtistName");
        var artistId = NugsShape.Str(r, "artistID", "ArtistID", "artistId");
        var venue = NugsShape.Str(r, "venue");
        var date = NugsShape.Str(r, "performanceDateFormatted", "performanceDate");
        var runtime = NugsShape.Str(r, "hhmmssTotalRunningTime");
        var img = NugsShape.Str(r?["img"], "url");

        var trackArr = NugsShape.Arr(r, "tracks", "Tracks", "songs", "Songs") ?? new JsonArray();
        var tracks = new List<TrackRow>();
        foreach (var t in trackArr)
        {
            var tid = NugsShape.Str(t, "trackID", "TrackID", "trackId", "id");
            if (string.IsNullOrEmpty(tid)) continue;
            var ttitle = NugsShape.Str(t, "songTitle", "SongTitle", "title");
            var trun = NugsShape.Str(t, "hhmmssTotalRunningTime");
            int.TryParse(NugsShape.Str(t, "setNum"), out var setNum);
            int.TryParse(NugsShape.Str(t, "trackNum"), out var trackNum);
            tracks.Add(new TrackRow(tid!, ttitle, trun, setNum, trackNum));
        }
        return new AlbumView(id, title, artist, artistId, date, venue, runtime, img, tracks);
    }

    /// <summary>Flattens a parsed album into a play queue, preserving track order.</summary>
    public static List<NowPlaying> ToQueue(AlbumView album) =>
        album.Tracks.Select(t => new NowPlaying(t.TrackId, t.Title, album.Artist, album.Title)).ToList();

    private static int CompareDateDesc(string? a, string? b) => ParseDate(b).CompareTo(ParseDate(a));

    private static DateTime ParseDate(string? s) =>
        DateTime.TryParseExact(s, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt : DateTime.MinValue;

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

using System.Text.Json.Nodes;

namespace Nugsdotnet.UI.Services;

/// <summary>
/// Singleton in-memory cache for catalog data that's expensive or annoying to
/// re-fetch on every navigation (e.g. the full artist list). Cleared on logout.
/// </summary>
public sealed class CatalogCache
{
    private readonly INugsGateway _api;
    public CatalogCache(INugsGateway api) => _api = api;

    private List<ArtistEntry>? _allArtists;
    private JsonNode? _allArtistsRaw;

    public bool HasArtists => _allArtists is not null;
    public IReadOnlyList<ArtistEntry>? AllArtists => _allArtists;
    public JsonNode? AllArtistsRaw => _allArtistsRaw;

    public async Task<IReadOnlyList<ArtistEntry>> GetAllArtistsAsync()
    {
        if (_allArtists is not null) return _allArtists;
        var raw = await _api.GetAllArtistsAsync();
        _allArtistsRaw = raw;
        _allArtists = ParseArtists(raw);
        return _allArtists;
    }

    public void Clear()
    {
        _allArtists = null;
        _allArtistsRaw = null;
    }

    /// <summary>
    /// Defensive parse over catalog.artists. Field names are guessed because
    /// we haven't captured this exact response yet — toggle the json button
    /// in any view if results look empty.
    /// </summary>
    private static List<ArtistEntry> ParseArtists(JsonNode? raw)
    {
        var r = NugsShape.Unwrap(raw);
        var arr = NugsShape.Arr(r,
            "artists", "Artists",
            "catalogArtists", "CatalogArtists",
            "items", "Items") ?? new JsonArray();

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

public sealed record ArtistEntry(string Id, string Name);

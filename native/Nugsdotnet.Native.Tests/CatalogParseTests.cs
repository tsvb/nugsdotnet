using System.Text.Json.Nodes;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class CatalogParseTests
{
    // ---- search ---------------------------------------------------------

    private const string SearchJson = """
    {
      "Response": {
        "catalogSearchTypeContainers": [{
          "catalogSearchContainers": [{
            "scHeader": "Artist: Goose",
            "catalogSearchResultItems": [
              { "artistID": "1", "artistName": "Goose" },
              { "artistID": "1", "artistName": "Goose", "containerID": "100",
                "containerName": "2023-06-24 The Capitol", "performanceDate": "6/24/2023", "venue": "The Capitol" }
            ]
          }]
        }]
      }
    }
    """;

    [Fact]
    public void ParseSearch_dedupes_artists_and_collects_containers()
    {
        var sv = NugsCatalog.ParseSearch(JsonNode.Parse(SearchJson));

        Assert.Single(sv.Artists);
        Assert.Equal("1", sv.Artists[0].Id);
        Assert.Equal("Goose", sv.Artists[0].Name);

        Assert.Single(sv.Sections);
        Assert.Equal("Artist: Goose", sv.Sections[0].Header);
        var item = Assert.Single(sv.Sections[0].Items);
        Assert.Equal("100", item.Id);
        Assert.True(item.IsShow);                       // has a performanceDate
        Assert.Equal("The Capitol", item.Venue);
    }

    [Fact]
    public void ParseSearch_skips_zero_container_ids()
    {
        var json = """
        { "Response": { "catalogSearchTypeContainers": [{ "catalogSearchContainers": [{
          "scHeader": "Songs",
          "catalogSearchResultItems": [ { "artistID": "5", "artistName": "Phish", "containerID": "0" } ]
        }]}]}}
        """;
        var sv = NugsCatalog.ParseSearch(JsonNode.Parse(json));
        Assert.Single(sv.Artists);
        Assert.Empty(sv.Sections);                      // container "0" filtered, section empty → dropped
    }

    // ---- artist shows ---------------------------------------------------

    private const string ArtistJson = """
    {
      "Response": {
        "containers": [
          { "artistName": "Goose", "containerID": "100", "containerInfo": "Shenanigans Nite",
            "img": { "url": "/images/a.jpg" } },
          { "artistName": "Goose", "containerID": "200", "containerInfo": "Live at X",
            "performanceDate": "6/24/2023", "venue": "X" },
          { "artistName": "Goose", "containerID": "201", "containerInfo": "Live at Y",
            "performanceDate": "7/1/2023", "venue": "Y" }
        ]
      }
    }
    """;

    [Fact]
    public void ParseArtistShows_splits_releases_from_shows_and_sorts_newest_first()
    {
        var a = NugsCatalog.ParseArtistShows(JsonNode.Parse(ArtistJson));

        Assert.Equal("Goose", a.ArtistName);

        var release = Assert.Single(a.Releases);
        Assert.Equal("100", release.Id);
        Assert.Equal("/images/a.jpg", release.ImagePath);
        Assert.False(release.IsShow);

        Assert.Equal(2, a.Shows.Count);
        Assert.Equal("201", a.Shows[0].Id);             // 7/1/2023 sorts before 6/24/2023
        Assert.Equal("200", a.Shows[1].Id);
    }

    // ---- album ----------------------------------------------------------

    private const string AlbumJson = """
    {
      "Response": {
        "containerID": "200",
        "containerInfo": "Live at X",
        "artistName": "Goose",
        "artistID": "1",
        "venue": "X",
        "performanceDateFormatted": "2023-06-24",
        "hhmmssTotalRunningTime": "1:23:45",
        "img": { "url": "/images/x.jpg" },
        "tracks": [
          { "trackID": "1001", "songTitle": "Song A", "hhmmssTotalRunningTime": "5:00", "setNum": "1", "trackNum": "1" },
          { "trackID": "1002", "songTitle": "Song B", "hhmmssTotalRunningTime": "6:30", "setNum": "1", "trackNum": "2" }
        ]
      }
    }
    """;

    [Fact]
    public void ParseAlbum_reads_header_and_tracks()
    {
        var album = NugsCatalog.ParseAlbum(JsonNode.Parse(AlbumJson));

        Assert.Equal("200", album.Id);
        Assert.Equal("Live at X", album.Title);
        Assert.Equal("Goose", album.Artist);
        Assert.Equal("1", album.ArtistId);
        Assert.Equal("/images/x.jpg", album.ImagePath);

        Assert.Equal(2, album.Tracks.Count);
        Assert.Equal("1001", album.Tracks[0].TrackId);
        Assert.Equal(1, album.Tracks[0].TrackNum);
        Assert.Equal("1. Song A", album.Tracks[0].Display);
    }

    [Fact]
    public void ToQueue_preserves_order_and_carries_album_context()
    {
        var album = NugsCatalog.ParseAlbum(JsonNode.Parse(AlbumJson));
        var queue = NugsCatalog.ToQueue(album);

        Assert.Equal(2, queue.Count);
        Assert.Equal("1001", queue[0].TrackId);
        Assert.Equal("Song A", queue[0].Title);
        Assert.Equal("Goose", queue[0].Artist);         // album artist
        Assert.Equal("Live at X", queue[0].Show);       // album title as display context
    }

    [Fact]
    public void ContainerEntry_subtitle_joins_present_fields_only()
    {
        var e = new ContainerEntry("1", "T", "Goose", "6/24/2023", null, null, true);
        Assert.Equal("6/24/2023  ·  Goose", e.Subtitle);   // null venue skipped
    }
}

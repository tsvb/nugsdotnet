using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class HomeDashboardTests
{
    private static ArtistEntry A(string id, string name) => new(id, name);

    // ---- greeting -------------------------------------------------------

    [Theory]
    [InlineData(5, "GOOD MORNING")]
    [InlineData(11, "GOOD MORNING")]
    [InlineData(12, "GOOD AFTERNOON")]
    [InlineData(17, "GOOD AFTERNOON")]
    [InlineData(18, "GOOD EVENING")]
    [InlineData(4, "GOOD EVENING")]
    public void GreetingFor_follows_the_faceplate_clock(int hour, string expected) =>
        Assert.Equal(expected, HomeDashboard.GreetingFor(hour));

    // ---- rail subtitle --------------------------------------------------

    [Fact]
    public void RailSubtitle_joins_date_venue_artist_and_skips_blanks()
    {
        Assert.Equal("6/24/2023  ·  The Capitol  ·  Goose",
            HomeDashboard.RailSubtitle("6/24/2023", "The Capitol", "Goose"));
        Assert.Equal("Phish", HomeDashboard.RailSubtitle(null, "  ", "Phish"));
        Assert.Null(HomeDashboard.RailSubtitle(null, null, null));
        Assert.Null(HomeDashboard.RailSubtitle("", "  ", null));
    }

    // ---- A–Z index ------------------------------------------------------

    [Theory]
    [InlineData("Goose", "G")]
    [InlineData(" phish", "P")]
    [InlineData("10,000 Maniacs", "#")]
    [InlineData("…And You Will Know", "#")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void IndexLetter_buckets_letters_and_non_letters(string? name, string? expected) =>
        Assert.Equal(expected, HomeDashboard.IndexLetter(name));

    [Fact]
    public void LettersFor_is_the_present_set_with_hash_first()
    {
        var letters = HomeDashboard.LettersFor(new[]
        {
            A("1", "Zebra"),
            A("2", "Goose"),
            A("3", "10,000 Maniacs"),
            A("4", "grateful dead"),
            A("5", "  "),
        });

        Assert.Equal(new[] { "#", "G", "Z" }, letters);
    }

    [Fact]
    public void FilterArtists_composes_letter_and_substring()
    {
        var all = new[]
        {
            A("1", "Goose"),
            A("2", "Grateful Dead"),
            A("3", "Greensky Bluegrass"),
            A("4", "Phish"),
        };

        Assert.Equal(new[] { "Goose", "Grateful Dead", "Greensky Bluegrass" },
            HomeDashboard.FilterArtists(all, null, "G").Select(a => a.Name));

        Assert.Equal(new[] { "Grateful Dead" },
            HomeDashboard.FilterArtists(all, "dead", "G").Select(a => a.Name));

        Assert.Equal(new[] { "Greensky Bluegrass" },
            HomeDashboard.FilterArtists(all, "sky", null).Select(a => a.Name));
    }

    [Fact]
    public void ArtistsLabel_shows_filtered_counts()
    {
        Assert.Equal("ARTISTS", HomeDashboard.ArtistsLabel(0, 0, false));
        Assert.Equal("ARTISTS · 380", HomeDashboard.ArtistsLabel(380, 380, false));
        Assert.Equal("ARTISTS · 14 OF 380", HomeDashboard.ArtistsLabel(14, 380, true));
        Assert.Equal("ARTISTS · 380", HomeDashboard.ArtistsLabel(380, 380, true));
    }

    [Fact]
    public void StashLabel_carries_the_full_count()
    {
        Assert.Equal("STASH", HomeDashboard.StashLabel(0));
        Assert.Equal("STASH · 18", HomeDashboard.StashLabel(18));
    }

    // ---- your artists ---------------------------------------------------

    [Fact]
    public void YourArtists_is_recents_then_stash_deduped_and_capped()
    {
        var catalog = new[]
        {
            A("g", "Goose"),
            A("p", "Phish"),
            A("w", "Widespread Panic"),
            A("k", "King Gizzard"),
        };

        var yours = HomeDashboard.YourArtists(
            recentArtists: new[] { "Goose", "Phish", "Goose" },
            stashArtists: new[] { "Widespread Panic", "Phish", "King Gizzard" },
            catalog,
            cap: 3);

        Assert.Equal(new[] { "g", "p", "w" }, yours.Select(a => a.Id));
    }

    [Fact]
    public void YourArtists_matches_catalog_names_case_insensitively_and_skips_unknowns()
    {
        var catalog = new[] { A("p", "Phish") };

        var yours = HomeDashboard.YourArtists(
            new[] { " phish ", "Unknown Band" },
            Array.Empty<string?>(),
            catalog);

        Assert.Equal("p", Assert.Single(yours).Id);
    }

    // ---- continue hero --------------------------------------------------

    [Fact]
    public void ContinueCue_is_deck_while_playing()
    {
        Assert.Equal("ON THE DECK", HomeDashboard.ContinueCue(isPlaying: true));
        Assert.Equal("CONTINUE", HomeDashboard.ContinueCue(isPlaying: false));
    }

    [Fact]
    public void FormatClock_matches_the_transport()
    {
        Assert.Equal("0:00", HomeDashboard.FormatClock(-1));
        Assert.Equal("0:00", HomeDashboard.FormatClock(double.NaN));
        Assert.Equal("1:05", HomeDashboard.FormatClock(65));
        Assert.Equal("125:30", HomeDashboard.FormatClock(125 * 60 + 30));
    }

    [Fact]
    public void ContinuePosition_prefers_clock_then_resume_prompt()
    {
        Assert.Equal("1:05 / 48:00", HomeDashboard.ContinuePosition(65, 48 * 60, primedPaused: true));
        Assert.Equal("press play to resume", HomeDashboard.ContinuePosition(0, 0, primedPaused: true));
        Assert.Equal("3:00", HomeDashboard.ContinuePosition(180, 0, primedPaused: false));
        Assert.Null(HomeDashboard.ContinuePosition(0, 0, primedPaused: false));
    }
}

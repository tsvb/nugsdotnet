using System.Globalization;

namespace Nugsdotnet.Native.Core;

/// <summary>Audio formats nugs serves, identified from the stream URL path.</summary>
public enum AudioFormat
{
    Unknown,
    Alac16,
    Flac16,
    Mqa24,
    S360Ra,
    Aac150,
    Hls,
}

/// <summary>A resolved stream: the signed CDN URL, the platformID that produced
/// it, and the format identified from the URL path.</summary>
public sealed record StreamPick(string Url, int PlatformId, AudioFormat Format);

// --- token / session wire + persistence shapes --------------------------------

/// <summary>OAuth token response from id.nugs.net (wire names are lowercase).</summary>
internal sealed record TokenResponse(string access_token, string refresh_token, int expires_in);

public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>Subscription info from subscriptions.nugs.net (wire names lowercase).</summary>
public sealed record SubInfo(
    string legacySubscriptionId,
    string startedAt,
    string endsAt,
    bool isContentAccessible,
    SubInfo.PlanInfo? plan,
    SubInfo.PromoInfo? promo)
{
    public sealed record PlanInfo(string id, string description);
    public sealed record PromoInfo(PlanInfo plan);
}

/// <summary>Everything persisted to disk (DPAPI-encrypted): tokens + identity + sub.</summary>
public sealed record PersistedSession(TokenSet Tokens, string UserId, SubInfo Sub);

/// <summary>
/// Flattened view used by the stream-resolution calls. Mirrors the original
/// Core's Session.From — promo plans nest one level deeper than normal plans.
/// </summary>
public sealed record Session(
    string AccessToken,
    string UserId,
    string SubscriptionId,
    string PlanId,
    long StartStamp,
    long EndStamp,
    string PlanDescription,
    bool IsAccessible)
{
    public static Session From(PersistedSession state)
    {
        var sub = state.Sub;
        var isPromo = sub.promo is not null;
        // promo's inner plan is non-nullable; only the non-promo plan can be absent
        // (free/expired/lapsed accounts), so guard it rather than force-unwrapping.
        var plan = isPromo ? sub.promo!.plan : sub.plan;
        var planId = plan?.id ?? string.Empty;
        var planDesc = plan?.description ?? string.Empty;
        return new Session(
            state.Tokens.AccessToken,
            state.UserId,
            sub.legacySubscriptionId,
            planId,
            ParseStamp(sub.startedAt),
            ParseStamp(sub.endsAt),
            planDesc,
            sub.isContentAccessible);
    }

    /// <summary>nugs returns timestamps as "MM/dd/yyyy HH:mm:ss" in UTC.</summary>
    private static long ParseStamp(string s)
    {
        var dt = DateTime.ParseExact(
            s, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
    }
}

// --- view models for the UI ---------------------------------------------------

/// <summary>Login state surfaced to the shell.</summary>
public sealed record SessionInfo(
    bool LoggedIn,
    string? UserId = null,
    string? Plan = null,
    bool Accessible = false);

public sealed record ArtistEntry(string Id, string Name);

/// <summary>A show or studio release as it appears in a list (search / artist page).</summary>
public sealed record ContainerEntry(
    string Id, string? Title, string? Artist, string? Date, string? Venue, string? ImagePath, bool IsShow)
{
    /// <summary>"date · venue · artist" with blanks skipped — for list rows.</summary>
    public string Subtitle =>
        string.Join("  ·  ", new[] { Date, Venue, Artist }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>One track row within an album/show.</summary>
public sealed record TrackRow(string TrackId, string? Title, string? RunTime, int SetNum, int TrackNum)
{
    /// <summary>"3. Song Title" (or just the title for studio tracks).</summary>
    public string Display => TrackNum > 0 ? $"{TrackNum}. {Title}" : (Title ?? "");
}

/// <summary>A fully-parsed album/show with its track list.</summary>
public sealed record AlbumView(
    string Id, string? Title, string? Artist, string? ArtistId,
    string? Date, string? Venue, string? RunTime, string? ImagePath,
    IReadOnlyList<TrackRow> Tracks);

/// <summary>One labelled section of search results (a scHeader + its items).</summary>
public sealed record SearchSection(string? Header, IReadOnlyList<ContainerEntry> Items);

/// <summary>Parsed catalog.search: the deduped artists plus the labelled sections.</summary>
public sealed record SearchView(IReadOnlyList<ArtistEntry> Artists, IReadOnlyList<SearchSection> Sections);

/// <summary>Parsed artist page: studio releases and live shows.</summary>
public sealed record ArtistShows(
    string? ArtistName, IReadOnlyList<ContainerEntry> Releases, IReadOnlyList<ContainerEntry> Shows);

/// <summary>One item in the play queue.</summary>
public sealed record NowPlaying(string TrackId, string? Title, string? Artist, string? Show);

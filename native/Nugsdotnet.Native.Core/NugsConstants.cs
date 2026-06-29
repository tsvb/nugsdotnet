namespace Nugsdotnet.Native.Core;

/// <summary>
/// Endpoint and client constants for the unofficial nugs.net API. These values
/// are community-documented (Sorrow446/Nugs-Downloader, Dniel97/orpheusdl-nugs)
/// and are reproduced here so this app has no dependency on any other project.
/// </summary>
public static class NugsConstants
{
    public const string ClientId = "Eg7HuH873H65r5rt325UytR5429";

    public const string MobileUserAgent =
        "NugsNet/3.26.724 (Android; 7.1.2; Asus; ASUS_Z01QD; Scale/2.0; en)";
    public const string LegacyUserAgent = "nugsnetAndroid";
    public const string PlayerReferer = "https://play.nugs.net/";

    public const string AuthUrl = "https://id.nugs.net/connect/token";
    public const string UserInfoUrl = "https://id.nugs.net/connect/userinfo";
    public const string SubInfoUrl = "https://subscriptions.nugs.net/api/v1/me/subscriptions";
    public const string StreamApiBase = "https://streamapi.nugs.net";
    public const string ImageCdnBase = "https://assets-01.nugscdn.net/livedownloads";

    /// <summary>
    /// platformID values passed to bigriver/subPlayer.aspx. These are opaque
    /// device tiers — each returns "some" URL whose real format is identified by
    /// the URL path (.flac16/, .alac16/, …), not by the id. We probe all four.
    /// </summary>
    public static readonly int[] ProbePlatforms = { 1, 4, 7, 10 };
}

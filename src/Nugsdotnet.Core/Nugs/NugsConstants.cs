namespace Nugsdotnet.Core.Nugs;

/// <summary>
/// Constants from the unofficial nugs.net mobile/web clients.
/// Sourced from Sorrow446/Nugs-Downloader and Dniel97/orpheusdl-nugs.
/// </summary>
public static class NugsConstants
{
    public const string ClientId = "Eg7HuH873H65r5rt325UytR5429";
    public const string DevKey = "x7f54tgbdyc64y656thy47er4";

    public const string MobileUserAgent =
        "NugsNet/3.26.724 (Android; 7.1.2; Asus; ASUS_Z01QD; Scale/2.0; en)";
    public const string LegacyUserAgent = "nugsnetAndroid";
    public const string PlayerReferer = "https://play.nugs.net/";

    public const string AuthUrl = "https://id.nugs.net/connect/token";
    public const string UserInfoUrl = "https://id.nugs.net/connect/userinfo";
    public const string SubInfoUrl = "https://subscriptions.nugs.net/api/v1/me/subscriptions";
    public const string StreamApiBase = "https://streamapi.nugs.net";

    /// <summary>
    /// platformID values for the bigriver/subPlayer.aspx endpoint.
    /// We probe in the order PROBE_PLATFORMS to discover availability,
    /// then prefer FLAC for browser playback.
    /// </summary>
    public static class Platform
    {
        public const int Alac16 = 1;
        public const int Flac16 = 2;
        public const int Mqa24 = 3;
        public const int S360Ra = 4;
        public const int Aac150 = 5;
        public const int Hls = 6;
    }

    public static readonly int[] ProbePlatforms = { 1, 4, 7, 10 };
}

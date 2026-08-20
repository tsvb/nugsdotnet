using System.Reflection;
using System.Text.Json;

namespace Nugsdotnet.Native.Services;

/// <summary>Checks GitHub Releases for a newer nugsdotnet build.</summary>
public sealed class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/tsvb/nugsdotnet/releases/latest";

    public Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("nugsdotnet");
            using var res = await http.GetAsync(ReleasesUrl, ct);
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            if (string.IsNullOrEmpty(tag) || !Version.TryParse(NormalizeVersion(tag), out var latest))
                return null;

            if (latest <= CurrentVersion) return null;

            var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            return new UpdateInfo(latest, tag, url);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Strips pre-release suffixes so Version.TryParse succeeds.</summary>
    private static string NormalizeVersion(string tag) =>
        tag.Split('-', '+')[0];
}

public sealed record UpdateInfo(Version Version, string Tag, string Url);

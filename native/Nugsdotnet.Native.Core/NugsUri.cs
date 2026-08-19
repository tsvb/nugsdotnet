using System.Diagnostics.CodeAnalysis;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// HTTPS URL checks for anything we fetch from a nugs response (CDN stream
/// links, catalog artwork). The API is untrusted input: a poisoned container
/// image path or streamLink must not become a loopback/file/http fetch.
/// </summary>
public static class NugsUri
{
    /// <summary>
    /// True for an absolute https URL with a DNS host and no userinfo.
    /// Rejects http, file, IP literals, localhost, and *.local — the shapes
    /// that would let catalog JSON steer the client at a local service.
    /// </summary>
    public static bool IsSafeHttps(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(parsed.UserInfo)) return false;
        if (parsed.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            return false;
        var host = parsed.IdnHost;
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    /// <summary>
    /// Absolute https URL, or a catalog-relative "/images/…" path on the image
    /// CDN. Returns null for anything that isn't a public https URL.
    /// </summary>
    public static string? ResolveImageUrl(string? pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return null;
        string url;
        if (pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            url = pathOrUrl;
        else if (pathOrUrl.StartsWith('/'))
            url = $"{NugsConstants.ImageCdnBase}{pathOrUrl}?h=400";
        else
            return null;
        return IsSafeHttps(url, out _) ? url : null;
    }
}

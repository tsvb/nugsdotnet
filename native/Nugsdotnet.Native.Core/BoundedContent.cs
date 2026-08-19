using System.Text;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// Reads an HTTP body with a hard byte cap so a misbehaving endpoint cannot
/// OOM the process. Used for catalog JSON and stream-probe payloads — audio
/// bytes go through <c>HttpAudioStream</c> with its own per-read cap.
/// </summary>
internal static class BoundedContent
{
    public const int Catalog = 8 * 1024 * 1024;
    public const int StreamProbe = 64 * 1024;
    public const int Auth = 16 * 1024;

    public static async Task<string> ReadStringAsync(
        HttpResponseMessage res, int maxBytes, CancellationToken ct)
    {
        if (res.Content.Headers.ContentLength is long len && len > maxBytes)
            throw new InvalidOperationException("response too large");

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 4096));
        var buf = new byte[8192];
        var total = 0;
        while (true)
        {
            var n = await stream.ReadAsync(buf, ct);
            if (n == 0) break;
            total += n;
            if (total > maxBytes) throw new InvalidOperationException("response too large");
            ms.Write(buf, 0, n);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}

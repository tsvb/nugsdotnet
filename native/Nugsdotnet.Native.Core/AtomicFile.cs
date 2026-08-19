namespace Nugsdotnet.Native.Core;

/// <summary>
/// Temp-file + rename so a crash mid-write cannot truncate the live file.
/// <see cref="StashStore"/> already had this; session/recents/playback
/// previously used <c>WriteAllBytes</c> and could load as corrupt-empty.
/// </summary>
internal static class AtomicFile
{
    public static async Task WriteAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, path, overwrite: true);
    }

    public static void Write(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}

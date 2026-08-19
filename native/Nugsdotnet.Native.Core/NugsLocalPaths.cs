using System.Security.Cryptography;
using System.Text;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// On-disk layout under %LOCALAPPDATA%\nugsdotnet. Session and window state stay
/// at the profile root (one live login, one window). Stash, recents, and
/// playback live under <c>accounts/{userId}/</c> so a second nugs login on the
/// same Windows profile does not inherit the previous listener's library.
/// </summary>
public static class NugsLocalPaths
{
    public const string AppFolder = "nugsdotnet";
    public const string AccountsFolder = "accounts";
    public const string StashFileName = "stash.json";
    public const string RecentsFileName = "recents.json";
    public const string PlaybackFileName = "playback.json";

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolder);

    public static string AccountDirectory(string root, string userId) =>
        Path.Combine(root, AccountsFolder, SanitizeUserId(userId));

    /// <summary>
    /// Filesystem-safe folder name for an OIDC <c>sub</c>. ASCII letters,
    /// digits, <c>.</c> <c>_</c> <c>-</c> pass through (typical GUIDs); anything
    /// else becomes a SHA-256 hex so <c>../</c> and punctuation cannot escape
    /// the accounts directory.
    /// </summary>
    public static string SanitizeUserId(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var trimmed = userId.Trim();
        if (trimmed.Length is > 0 and <= 64 && IsSafeSegment(trimmed))
            return trimmed;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Moves pre-account <c>stash.json</c> / <c>recents.json</c> /
    /// <c>playback.json</c> from the profile root into <paramref name="accountDir"/>
    /// when that file is not already there. First login after the upgrade claims
    /// them; later accounts start empty.
    /// </summary>
    public static void MigrateLegacy(string root, string accountDir)
    {
        Directory.CreateDirectory(accountDir);
        foreach (var name in new[] { StashFileName, RecentsFileName, PlaybackFileName })
        {
            var src = Path.Combine(root, name);
            var dst = Path.Combine(accountDir, name);
            if (!File.Exists(src) || File.Exists(dst)) continue;
            try
            {
                File.Move(src, dst);
            }
            catch
            {
                // Leave the root file; the account just starts empty.
            }
        }
    }

    private static bool IsSafeSegment(string s)
    {
        foreach (var c in s)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') continue;
            return false;
        }
        return s is not "." and not "..";
    }
}

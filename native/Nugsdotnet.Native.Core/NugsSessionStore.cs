using System.Security.Cryptography;
using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// File-backed persistence for the nugs session. Upgrade over the original
/// plaintext tokens.json: the blob is encrypted at rest with Windows DPAPI
/// (CurrentUser scope) plus app-specific entropy, so a copied file can't be
/// read on another machine/account and a generic DPAPI dump of the user store
/// isn't enough on its own.
///
/// On non-Windows (dev/CI of the Core library) it falls back to an unencrypted
/// envelope — the shipping app only ever runs on Windows, where DPAPI is used.
///
/// v1 blobs start with the ASCII prefix <c>NDS1</c>. Legacy files (DPAPI with
/// no entropy, or raw JSON in CI) still load; the next successful read rewrites
/// them in v1 so existing sessions are not invalidated.
/// </summary>
public sealed class NugsSessionStore
{
    /// <summary>v1 envelope marker. Not a secret — tells Load which Unprotect to use.</summary>
    internal static ReadOnlySpan<byte> V1Prefix => "NDS1"u8;

    /// <summary>
    /// Mixing bytes for DPAPI optionalEntropy. Not a password: the key is still
    /// the Windows user, this just scopes the blob to this app.
    /// </summary>
    internal static readonly byte[] Entropy = "nugsdotnet.session.v1"u8.ToArray();

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PersistedSession? _cache;

    /// <summary>Default location: %LOCALAPPDATA%\nugsdotnet\session.bin.</summary>
    public NugsSessionStore()
        : this(Path.Combine(NugsLocalPaths.DefaultRoot, "session.bin"))
    {
    }

    public NugsSessionStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task<PersistedSession?> LoadAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is not null) return _cache;     // re-check under lock
            if (!File.Exists(_path)) return null;
            var blob = await File.ReadAllBytesAsync(_path, ct);
            if (!TryDecrypt(blob, out var json, out var legacy)) return null;
            _cache = JsonSerializer.Deserialize<PersistedSession>(json);
            if (_cache is not null && legacy)
            {
                try
                {
                    await AtomicFile.WriteAsync(_path, Encrypt(json), ct);
                }
                catch
                {
                    // Stay logged in; the next SaveAsync will retry the v1 rewrite.
                }
            }
            return _cache;
        }
        catch
        {
            return null;   // corrupt/unreadable — treat as logged out
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(PersistedSession state, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(state);
            await AtomicFile.WriteAsync(_path, Encrypt(json), ct);
            _cache = state;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _cache = null;
            if (File.Exists(_path)) File.Delete(_path);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>v0 blob: DPAPI with no entropy (Windows) or raw JSON (CI). Used to
    /// prove Load still opens pre-entropy sessions and rewrites them as v1.</summary>
    internal static byte[] EncryptLegacy(byte[] plaintext)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return plaintext;
    }

    internal static byte[] Encrypt(byte[] plaintext)
    {
        var protectedBytes = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser)
            : plaintext;
        var blob = new byte[V1Prefix.Length + protectedBytes.Length];
        V1Prefix.CopyTo(blob);
        Buffer.BlockCopy(protectedBytes, 0, blob, V1Prefix.Length, protectedBytes.Length);
        return blob;
    }

    /// <summary>
    /// <paramref name="legacy"/> is true when the blob was v0 (no prefix / no
    /// entropy) and should be rewritten as v1.
    /// </summary>
    internal static bool TryDecrypt(byte[] blob, out byte[] json, out bool legacy)
    {
        json = Array.Empty<byte>();
        legacy = false;
        if (blob.Length >= V1Prefix.Length && blob.AsSpan().StartsWith(V1Prefix))
        {
            var inner = blob.AsSpan(V1Prefix.Length).ToArray();
            try
            {
                json = Unprotect(inner, Entropy);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        try
        {
            json = Unprotect(blob, optionalEntropy: null);
            legacy = true;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static byte[] Unprotect(byte[] blob, byte[]? optionalEntropy)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(blob, optionalEntropy, DataProtectionScope.CurrentUser);
        return blob;   // dev/CI fallback only
    }
}

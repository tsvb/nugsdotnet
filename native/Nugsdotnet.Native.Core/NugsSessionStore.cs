using System.Security.Cryptography;
using System.Text.Json;

namespace Nugsdotnet.Native.Core;

/// <summary>
/// File-backed persistence for the nugs session. Upgrade over the original
/// plaintext tokens.json: the blob is encrypted at rest with Windows DPAPI
/// (CurrentUser scope), so a copied file can't be read on another machine/account.
///
/// On non-Windows (dev/CI of the Core library) it falls back to plaintext — the
/// shipping app only ever runs on Windows, where the encrypted path is taken.
/// </summary>
public sealed class NugsSessionStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PersistedSession? _cache;

    /// <summary>Default location: %LOCALAPPDATA%\nugsdotnet\session.bin.</summary>
    public NugsSessionStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nugsdotnet", "session.bin"))
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
            var json = Decrypt(blob);
            _cache = JsonSerializer.Deserialize<PersistedSession>(json);
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
            await File.WriteAllBytesAsync(_path, Encrypt(json), ct);
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

    private static byte[] Encrypt(byte[] plaintext)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return plaintext;   // dev/CI fallback only
    }

    private static byte[] Decrypt(byte[] blob)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(blob, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return blob;
    }
}

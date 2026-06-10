using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Nugsdotnet.Core.Nugs;

/// <summary>
/// File-backed persistence for the nugs session. v0.1 stores plaintext JSON
/// in tokens.json next to the server. Fine for a personal local tool;
/// .gitignored.
/// </summary>
public sealed class TokenStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PersistedSession? _cache;

    /// <summary>Web-host constructor — DI resolves this; tokens.json lives in the content root.</summary>
    public TokenStore(IHostEnvironment env)
        : this(Path.Combine(env.ContentRootPath, "tokens.json"))
    {
    }

    /// <summary>
    /// Explicit-path constructor for the MAUI head, which stores tokens in
    /// the per-platform app-data directory rather than next to an exe.
    /// </summary>
    public TokenStore(string path)
    {
        _path = path;
    }

    public async Task<PersistedSession?> LoadAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;
        if (!File.Exists(_path)) return null;
        await _lock.WaitAsync(ct);
        try
        {
            await using var fs = File.OpenRead(_path);
            _cache = await JsonSerializer.DeserializeAsync<PersistedSession>(
                fs, cancellationToken: ct);
            return _cache;
        }
        catch
        {
            return null;
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
            await using var fs = File.Create(_path);
            await JsonSerializer.SerializeAsync(
                fs, state, new JsonSerializerOptions { WriteIndented = true }, ct);
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
}

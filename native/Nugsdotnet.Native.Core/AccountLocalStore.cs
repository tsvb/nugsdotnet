namespace Nugsdotnet.Native.Core;

/// <summary>
/// Points stash / recents / playback at the signed-in nugs account. Unbound
/// (signed out) paths are null so those stores load empty and refuse writes.
/// </summary>
public sealed class AccountLocalStore
{
    private readonly object _gate = new();
    private readonly string _root;
    private string? _userId;

    public AccountLocalStore() : this(NugsLocalPaths.DefaultRoot) { }

    public AccountLocalStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string? UserId
    {
        get { lock (_gate) return _userId; }
    }

    /// <summary>Scope local library files to this nugs user. Migrates legacy
    /// profile-root JSON into the account folder on first bind.</summary>
    public void Bind(string userId)
    {
        var id = NugsLocalPaths.SanitizeUserId(userId);
        var dir = NugsLocalPaths.AccountDirectory(_root, userId);
        NugsLocalPaths.MigrateLegacy(_root, dir);
        lock (_gate) _userId = id;
    }

    public void Unbind()
    {
        lock (_gate) _userId = null;
    }

    /// <summary>Absolute path under the bound account, or null when signed out.</summary>
    public string? File(string fileName)
    {
        lock (_gate)
        {
            if (_userId is null) return null;
            return Path.Combine(NugsLocalPaths.AccountDirectory(_root, _userId), fileName);
        }
    }
}

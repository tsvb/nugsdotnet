using System.Text.Json;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native;

/// <summary>User preferences persisted at %LOCALAPPDATA%\nugsdotnet\settings.json.</summary>
public sealed record AppSettings(
    AudioFormat? PreferredFormat = null,
    double DefaultVolume = 1.0);

/// <summary>Loads and saves app-wide preferences (not account-scoped).</summary>
public sealed class SettingsStore
{
    private static readonly string PathOnDisk = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nugsdotnet", "settings.json");

    private AppSettings _current = new();

    public AppSettings Current => _current;

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return;
            var json = await File.ReadAllBytesAsync(PathOnDisk);
            _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            _current = _current with { DefaultVolume = Math.Clamp(_current.DefaultVolume, 0, 1) };
        }
        catch
        {
            _current = new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        _current = settings with { DefaultVolume = Math.Clamp(settings.DefaultVolume, 0, 1) };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
            await File.WriteAllBytesAsync(PathOnDisk, JsonSerializer.SerializeToUtf8Bytes(_current));
        }
        catch
        {
        }
    }
}

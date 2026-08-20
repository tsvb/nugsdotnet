using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>App preferences: stream format and default volume.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly PlayerService _player;

    [ObservableProperty] public partial string? SelectedFormat { get; set; } = "auto";
    [ObservableProperty] public partial double DefaultVolumePercent { get; set; } = 100;
    [ObservableProperty] public partial string? Status { get; set; }

    public IReadOnlyList<string> FormatOptions { get; } =
        ["auto", "flac16", "mqa24", "alac16", "aac150", "hls"];

    public SettingsViewModel(SettingsStore settings, PlayerService player)
    {
        _settings = settings;
        _player = player;
    }

    public void Load()
    {
        var s = _settings.Current;
        SelectedFormat = s.PreferredFormat?.ToString().ToLowerInvariant() ?? "auto";
        DefaultVolumePercent = s.DefaultVolume * 100.0;
        Status = null;
    }

    public async Task SaveAsync()
    {
        AudioFormat? format = SelectedFormat switch
        {
            "flac16" => AudioFormat.Flac16,
            "mqa24" => AudioFormat.Mqa24,
            "alac16" => AudioFormat.Alac16,
            "aac150" => AudioFormat.Aac150,
            "hls" => AudioFormat.Hls,
            _ => null,
        };
        var settings = new AppSettings(format, DefaultVolumePercent / 100.0);
        await _settings.SaveAsync(settings);
        _player.Volume = settings.DefaultVolume;
        Status = "Settings saved.";
    }
}

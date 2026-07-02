using System.Text.Json;

namespace Nugsdotnet.Native;

/// <summary>Window geometry + shell layout that survive a restart.</summary>
public sealed record WindowState(int X, int Y, int Width, int Height, bool DashboardOpen);

/// <summary>
/// Tiny synchronous store for window state at
/// %LOCALAPPDATA%\nugsdotnet\window.json. Synchronous on purpose: it's read
/// once during window construction and written once from the Closed handler —
/// no async machinery warranted. Failures are swallowed; worst case the window
/// opens at defaults.
/// </summary>
public static class WindowStateStore
{
    private static readonly string PathOnDisk = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nugsdotnet", "window.json");

    public static WindowState? TryLoad()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return null;
            return JsonSerializer.Deserialize<WindowState>(File.ReadAllBytes(PathOnDisk));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WindowState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
            File.WriteAllBytes(PathOnDisk, JsonSerializer.SerializeToUtf8Bytes(state));
        }
        catch
        {
        }
    }
}

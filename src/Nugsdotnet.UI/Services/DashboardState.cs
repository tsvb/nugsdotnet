using Microsoft.JSInterop;

namespace Nugsdotnet.UI.Services;

/// <summary>
/// Holds the dashboard panel's UI state — open/closed plus a collapsed flag per
/// section — and persists it to localStorage. Scoped, like the other client
/// services, so it's effectively a singleton for the page lifetime: the topbar
/// toggle and the panel share one instance and both re-render via
/// <see cref="StateChanged"/>.
/// </summary>
public sealed class DashboardState
{
    private const string PanelKey = "nugs:dashboard:open";
    private const string SectionsKey = "nugs:dashboard:sections";

    private IJSRuntime? _js;

    public bool PanelOpen { get; private set; } = true;

    /// <summary>Per-section open flag: 0 Now Playing, 1 Up Next, 2 Quality, 3 Telemetry.</summary>
    public bool[] SectionOpen { get; } = { true, true, true, true };

    public event Action? StateChanged;

    /// <summary>
    /// Loads persisted state from localStorage. Call after first render (the
    /// post-render JS-interop point, mirroring MainLayout's key binding).
    /// </summary>
    public async Task RestoreAsync(IJSRuntime js)
    {
        _js = js;
        try
        {
            var panel = await js.InvokeAsync<string?>("audioInterop.lsGet", PanelKey);
            if (panel is "0" or "1") PanelOpen = panel == "1";

            var sections = await js.InvokeAsync<string?>("audioInterop.lsGet", SectionsKey);
            if (!string.IsNullOrEmpty(sections))
            {
                var parts = sections.Split(',');
                for (var i = 0; i < Math.Min(parts.Length, SectionOpen.Length); i++)
                {
                    if (parts[i] is "0" or "1") SectionOpen[i] = parts[i] == "1";
                }
            }
        }
        catch
        {
            // localStorage may be unavailable (private mode) — keep defaults.
        }
        StateChanged?.Invoke();
    }

    public void TogglePanel()
    {
        PanelOpen = !PanelOpen;
        StateChanged?.Invoke();
        _ = PersistAsync();
    }

    public void ToggleSection(int i)
    {
        if (i < 0 || i >= SectionOpen.Length) return;
        SectionOpen[i] = !SectionOpen[i];
        StateChanged?.Invoke();
        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        if (_js is null) return;
        try
        {
            await _js.InvokeVoidAsync("audioInterop.lsSet", PanelKey, PanelOpen ? "1" : "0");
            await _js.InvokeVoidAsync("audioInterop.lsSet", SectionsKey,
                string.Join(",", SectionOpen.Select(b => b ? "1" : "0")));
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}

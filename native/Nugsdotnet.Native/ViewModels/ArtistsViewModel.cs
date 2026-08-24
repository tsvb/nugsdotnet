using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>
/// The A–Z artist index on its own page: live filter, letter buckets, and the
/// virtualized grid. State moved here from HomeViewModel when Home became the
/// listening journal. Singleton — the catalog fetch is shared with Home's
/// "your artists" derivation.
/// </summary>
public partial class ArtistsViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private List<ArtistEntry> _all = new();

    public ObservableCollection<ArtistEntry> Artists { get; } = new();
    public ObservableCollection<LetterChip> Letters { get; } = new();

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial string? ActiveLetter { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string ArtistsLabel { get; set; } = "ARTISTS";
    [ObservableProperty] public partial bool IsFiltered { get; set; }

    public ArtistsViewModel(NugsCatalog catalog) => _catalog = catalog;

    /// <summary>Artists fetched so far this session (empty until first load).</summary>
    public IReadOnlyList<ArtistEntry> CatalogSnapshot() => _all;

    public async Task LoadArtistsAsync(bool force = false)
    {
        if (!force && _all.Count > 0)
        {
            RebuildLetters();
            ApplyFilter();
            return;
        }
        Busy = true;
        Status = null;
        try
        {
            _all = NugsCatalog.ParseArtists(await _catalog.GetAllArtistsAsync());
            RebuildLetters();
            ApplyFilter();
            if (_all.Count == 0) Status = "No artists returned.";
        }
        catch (Exception ex)
        {
            Status = UserError.From(ex);
        }
        finally
        {
            Busy = false;
        }
    }

    public Task ReloadArtistsAsync() => LoadArtistsAsync(force: true);

    /// <summary>Toggles the A–Z bucket. Pressing the active letter clears it.</summary>
    public void ToggleLetter(string? letter)
    {
        ActiveLetter = string.Equals(ActiveLetter, letter, StringComparison.Ordinal)
            ? null : letter;
        RebuildLetters();
        ApplyFilter();
    }

    public void ClearFilters()
    {
        Filter = "";
        ActiveLetter = null;
        RebuildLetters();
        ApplyFilter();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = !string.IsNullOrWhiteSpace(Filter) || !string.IsNullOrWhiteSpace(ActiveLetter);
        IsFiltered = filtered;
        Artists.Clear();
        foreach (var a in HomeDashboard.FilterArtists(_all, Filter, ActiveLetter))
            Artists.Add(a);
        ArtistsLabel = HomeDashboard.ArtistsLabel(Artists.Count, _all.Count, filtered);
    }

    private void RebuildLetters()
    {
        Letters.Clear();
        foreach (var letter in HomeDashboard.LettersFor(_all))
            Letters.Add(new LetterChip { Letter = letter, IsActive = letter == ActiveLetter });
    }
}

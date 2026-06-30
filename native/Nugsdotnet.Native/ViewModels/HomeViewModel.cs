using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Landing page: the full artist list with a live filter box.</summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private List<ArtistEntry> _all = new();

    public ObservableCollection<ArtistEntry> Artists { get; } = new();

    [ObservableProperty] private string filter = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? status;

    public HomeViewModel(NugsCatalog catalog) => _catalog = catalog;

    public async Task LoadAsync()
    {
        if (_all.Count > 0) return;   // cached for the session
        Busy = true;
        Status = null;
        try
        {
            _all = NugsCatalog.ParseArtists(await _catalog.GetAllArtistsAsync());
            ApplyFilter();
            if (_all.Count == 0) Status = "No artists returned.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Artists.Clear();
        IEnumerable<ArtistEntry> q = _all;
        if (!string.IsNullOrWhiteSpace(Filter))
            q = _all.Where(a => a.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));
        foreach (var a in q) Artists.Add(a);
    }
}

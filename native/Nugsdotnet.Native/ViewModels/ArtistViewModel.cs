using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Artist page: studio releases + live shows (newest first).</summary>
public partial class ArtistViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;

    public ObservableCollection<ContainerEntry> Releases { get; } = new();
    public ObservableCollection<ContainerEntry> Shows { get; } = new();

    [ObservableProperty] private string? artistName;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? status;

    public ArtistViewModel(NugsCatalog catalog) => _catalog = catalog;

    public async Task LoadAsync(string artistId)
    {
        Busy = true;
        Status = null;
        Releases.Clear();
        Shows.Clear();
        try
        {
            var data = NugsCatalog.ParseArtistShows(await _catalog.GetArtistShowsAsync(artistId));
            ArtistName = data.ArtistName;
            foreach (var r in data.Releases) Releases.Add(r);
            foreach (var s in data.Shows) Shows.Add(s);
            if (Releases.Count == 0 && Shows.Count == 0) Status = "No shows or releases found.";
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
}

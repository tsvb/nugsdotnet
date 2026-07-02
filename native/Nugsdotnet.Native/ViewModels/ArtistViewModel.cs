using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Artist page: studio releases (art cards) + live shows (newest first).</summary>
public partial class ArtistViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;
    private readonly ImageLoader _images;

    /// <summary>Releases as art cards — usually a short rail, unlike the show list.</summary>
    public ObservableCollection<ShowCard> Releases { get; } = new();
    public ObservableCollection<ContainerEntry> Shows { get; } = new();

    [ObservableProperty] private string? artistName;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? status;

    public ArtistViewModel(NugsCatalog catalog, ImageLoader images)
    {
        _catalog = catalog;
        _images = images;
    }

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
            // Sub is omitted — every card on this page belongs to the artist heading.
            foreach (var r in data.Releases) Releases.Add(new ShowCard(r.Id, r.Title, null, r.ImagePath));
            foreach (var s in data.Shows) Shows.Add(s);
            if (Releases.Count == 0 && Shows.Count == 0) Status = "No shows or releases found.";
            _ = LoadArtsAsync(Releases.ToList());   // fills in as it downloads; never throws
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

    private async Task LoadArtsAsync(IReadOnlyList<ShowCard> cards)
    {
        foreach (var card in cards) await card.LoadArtAsync(_images);
    }
}

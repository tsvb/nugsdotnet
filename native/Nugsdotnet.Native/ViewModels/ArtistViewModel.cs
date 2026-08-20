using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Artist page: studio releases (art cards) + live shows (newest first).</summary>
public partial class ArtistViewModel : ObservableObject
{
    private const int PageSize = 100;

    private readonly NugsCatalog _catalog;
    private readonly ImageLoader _images;
    private string? _artistId;
    private int _nextOffset = 1;

    /// <summary>Releases as art cards — usually a short rail, unlike the show list.</summary>
    public ObservableCollection<ShowCard> Releases { get; } = new();
    public ObservableCollection<ContainerEntry> Shows { get; } = new();

    [ObservableProperty] public partial string? ArtistName { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool LoadingMore { get; set; }
    [ObservableProperty] public partial bool HasMoreShows { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }

    public ArtistViewModel(NugsCatalog catalog, ImageLoader images)
    {
        _catalog = catalog;
        _images = images;
    }

    public async Task LoadAsync(string artistId)
    {
        _artistId = artistId;
        _nextOffset = 1;
        Busy = true;
        Status = null;
        HasMoreShows = false;
        Releases.Clear();
        Shows.Clear();
        try
        {
            await LoadPageAsync(isInitial: true);
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

    public async Task LoadMoreShowsAsync()
    {
        if (_artistId is null || !HasMoreShows || LoadingMore) return;
        LoadingMore = true;
        try
        {
            await LoadPageAsync(isInitial: false);
        }
        catch (Exception ex)
        {
            Status = UserError.From(ex);
        }
        finally
        {
            LoadingMore = false;
        }
    }

    private async Task LoadPageAsync(bool isInitial)
    {
        if (_artistId is null) return;
        var data = NugsCatalog.ParseArtistShows(
            await _catalog.GetArtistShowsAsync(_artistId, _nextOffset, PageSize));
        if (isInitial)
        {
            ArtistName = data.ArtistName;
            foreach (var r in data.Releases) Releases.Add(new ShowCard(r.Id, r.Title, null, r.ImagePath));
            _ = LoadArtsAsync(Releases.ToList());
        }
        foreach (var s in data.Shows) Shows.Add(s);
        _nextOffset += data.Shows.Count;
        HasMoreShows = data.Shows.Count >= PageSize;
        if (isInitial && Releases.Count == 0 && Shows.Count == 0)
            Status = "No shows or releases found.";
    }

    private async Task LoadArtsAsync(IReadOnlyList<ShowCard> cards) =>
        await Task.WhenAll(cards.Select(c => c.LoadArtAsync(_images)));
}

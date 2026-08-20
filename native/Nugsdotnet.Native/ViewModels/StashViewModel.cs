using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Imaging;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Full stash library — every stashed show/release, not just the home rail cap.</summary>
public partial class StashViewModel : ObservableObject
{
    private readonly StashStore _stash;
    private readonly ImageLoader _images;

    public ObservableCollection<ShowCard> Items { get; } = new();

    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string Heading { get; set; } = "STASH";

    public StashViewModel(StashStore stash, ImageLoader images)
    {
        _stash = stash;
        _images = images;
    }

    public async Task LoadAsync()
    {
        Busy = true;
        Status = null;
        Items.Clear();
        try
        {
            var all = await _stash.LoadAsync();
            foreach (var s in all)
                Items.Add(new ShowCard(s.ContainerId, s.Title, s.Artist, s.ImagePath));
            Heading = all.Count > 0 ? $"STASH · {all.Count}" : "STASH";
            if (all.Count == 0) Status = "Nothing stashed yet — star a show on its album page.";
            _ = LoadArtsAsync(Items.ToList());
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

    private async Task LoadArtsAsync(IReadOnlyList<ShowCard> cards) =>
        await Task.WhenAll(cards.Select(c => c.LoadArtAsync(_images)));
}

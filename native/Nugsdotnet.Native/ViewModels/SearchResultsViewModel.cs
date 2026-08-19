using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Search results: matched artists plus shows/releases to drill into.</summary>
public partial class SearchResultsViewModel : ObservableObject
{
    private readonly NugsCatalog _catalog;

    public ObservableCollection<ArtistEntry> Artists { get; } = new();
    public ObservableCollection<ContainerEntry> Containers { get; } = new();

    [ObservableProperty] public partial string? Heading { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }

    public SearchResultsViewModel(NugsCatalog catalog) => _catalog = catalog;

    public async Task LoadAsync(string query)
    {
        Heading = $"Results for “{query}”";
        Busy = true;
        Status = null;
        Artists.Clear();
        Containers.Clear();
        try
        {
            var sv = NugsCatalog.ParseSearch(await _catalog.SearchAsync(query));
            foreach (var a in sv.Artists) Artists.Add(a);
            foreach (var sec in sv.Sections)
                foreach (var c in sec.Items) Containers.Add(c);
            if (Artists.Count == 0 && Containers.Count == 0) Status = "No results.";
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
}

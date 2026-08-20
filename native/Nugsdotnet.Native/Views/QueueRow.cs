using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Nugsdotnet.Native.Views;

/// <summary>One row of the up-next list. Rebuilt wholesale on queue change, so
/// plain get-only properties are enough for compiled bindings.</summary>
public sealed class QueueRow
{
    public int Index { get; init; }
    public string Title { get; init; } = "";
    public bool IsCurrent { get; init; }

    public string Position => $"{Index + 1:00}";
    public Visibility CurrentMarkerVisibility =>
        IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    public Brush TitleBrush =>
        (Brush)Application.Current.Resources[IsCurrent ? "BrandAccent" : "BrandText"];
}

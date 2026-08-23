using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>One A–Z (or #) jump key on the Home artist index. Rebuilt when
/// the active letter changes, so get-only properties are enough for x:Bind.</summary>
public sealed class LetterChip
{
    public string Letter { get; init; } = "";
    public bool IsActive { get; init; }

    public Brush Foreground =>
        (Brush)Application.Current.Resources[IsActive ? "BrandAccent" : "BrandDim"];
}

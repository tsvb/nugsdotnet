using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Nugsdotnet.Native.Views.Controls;

/// <summary>
/// One VU meter on the journal hero: a printed scale, a needle resting at the
/// current level, and a red zone marking the next milestone's approach.
/// </summary>
public sealed partial class VuMeter : UserControl
{
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(VuMeter), new PropertyMetadata("", OnLayoutChanged));
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(VuMeter), new PropertyMetadata("", OnLayoutChanged));
    public static readonly DependencyProperty ScaleEndProperty = DependencyProperty.Register(
        nameof(ScaleEnd), typeof(string), typeof(VuMeter), new PropertyMetadata("", OnLayoutChanged));
    public static readonly DependencyProperty NeedleProperty = DependencyProperty.Register(
        nameof(Needle), typeof(double), typeof(VuMeter), new PropertyMetadata(0.0, OnLayoutChanged));
    public static readonly DependencyProperty RedZoneProperty = DependencyProperty.Register(
        nameof(RedZone), typeof(double), typeof(VuMeter), new PropertyMetadata(0.85, OnLayoutChanged));

    public VuMeter()
    {
        InitializeComponent();
        Layout();
    }

    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string ValueText { get => (string)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public string ScaleEnd { get => (string)GetValue(ScaleEndProperty); set => SetValue(ScaleEndProperty, value); }
    public double Needle { get => (double)GetValue(NeedleProperty); set => SetValue(NeedleProperty, value); }
    public double RedZone { get => (double)GetValue(RedZoneProperty); set => SetValue(RedZoneProperty, value); }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((VuMeter)d).Layout();

    private void OnScaleSizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void Layout()
    {
        CaptionText.Text = Caption;
        ValueTextEl.Text = ValueText;
        ScaleEndText.Text = ScaleEnd;
        ScaleEndText.Visibility = string.IsNullOrEmpty(ScaleEnd) ? Visibility.Collapsed : Visibility.Visible;

        var w = ScaleGrid.ActualWidth;
        if (double.IsNaN(w) || w <= 0) return;
        RedZoneRect.Width = Math.Clamp(RedZone, 0, 1) * w;
        RedZoneRect.Visibility = RedZone is > 0 and < 1 ? Visibility.Visible : Visibility.Collapsed;
        NeedleRect.Margin = new Thickness(Math.Clamp(Needle, 0, 1) * w - 1, 0, 0, 0);
    }
}

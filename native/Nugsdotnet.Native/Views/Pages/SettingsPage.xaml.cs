using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm;

    public SettingsPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<SettingsViewModel>();
        DataContext = _vm;
        FormatBox.ItemsSource = _vm.FormatOptions;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _vm.Load();
        FormatBox.SelectedItem = _vm.SelectedFormat;
    }

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormatBox.SelectedItem is string s) _vm.SelectedFormat = s;
    }

    private async void OnSave(object sender, RoutedEventArgs e) => await _vm.SaveAsync();
}

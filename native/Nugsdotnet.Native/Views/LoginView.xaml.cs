using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nugsdotnet.Native.ViewModels;

namespace Nugsdotnet.Native.Views;

public sealed partial class LoginView : UserControl
{
    private readonly LoginViewModel _vm;

    /// <summary>Raised after a successful sign-in so the shell can swap to the app.</summary>
    public event EventHandler? LoggedIn;

    public LoginView()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<LoginViewModel>();
        DataContext = _vm;
    }

    // PasswordBox.Password doesn't round-trip through a binding reliably, so we
    // push it into the view model on every change.
    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _vm.Password = PasswordInput.Password;

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (await _vm.SignInAsync())
            LoggedIn?.Invoke(this, EventArgs.Empty);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nugsdotnet.Native.ViewModels;
using Windows.System;

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

    private void OnCredentialKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        _ = SubmitAsync();
    }

    private async void OnSignIn(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        // Read the controls directly: TextBox TwoWay bindings update on lost
        // focus, so Enter-from-the-email-field would otherwise submit stale text.
        _vm.Email = EmailInput.Text ?? "";
        _vm.Password = PasswordInput.Password;
        if (await _vm.SignInAsync())
        {
            PasswordInput.Password = "";
            LoggedIn?.Invoke(this, EventArgs.Empty);
        }
    }
}

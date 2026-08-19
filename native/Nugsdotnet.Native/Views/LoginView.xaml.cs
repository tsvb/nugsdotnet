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
        Loaded += (_, _) => EmailInput.Focus(FocusState.Programmatic);
    }

    private void OnCredentialKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        _ = SubmitAsync();
    }

    private async void OnSignIn(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        // Read the controls here, not via bindings: TextBox TwoWay updates on
        // lost focus (Enter from email would submit stale text), and the
        // password is never copied onto the view model.
        var email = EmailInput.Text ?? "";
        var password = PasswordInput.Password;
        if (await _vm.SignInAsync(email, password))
        {
            PasswordInput.Password = "";
            LoggedIn?.Invoke(this, EventArgs.Empty);
        }
    }
}

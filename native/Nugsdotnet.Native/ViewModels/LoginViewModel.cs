using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Login form state. Credentials come only from the typed fields —
/// the process environment is never read for a password.</summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly NugsAuth _auth;

    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsEnabled))]
    public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Error { get; set; };

    public bool FieldsEnabled => !Busy;

    public LoginViewModel(NugsAuth auth) => _auth = auth;

    /// <summary>Attempts sign-in. Returns true on success.</summary>
    public async Task<bool> SignInAsync()
    {
        if (Busy) return false;
        Busy = true;
        Error = null;
        try
        {
            var e = Email.Trim();
            var p = Password;
            if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p))
            {
                Error = "Enter email and password.";
                return false;
            }
            await _auth.LoginAsync(e, p);
            Password = "";
            return true;
        }
        catch (Exception ex)
        {
            Error = UserError.From(ex);
            return false;
        }
        finally
        {
            Busy = false;
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Login form state. Credentials come only from the typed fields —
/// the process environment is never read for a password, and the password is
/// never stored on this object (it is an argument to <see cref="SignInAsync"/>).</summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly NugsAuth _auth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsEnabled))]
    public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    public bool FieldsEnabled => !Busy;

    public LoginViewModel(NugsAuth auth) => _auth = auth;

    /// <summary>Attempts sign-in. Returns true on success. The password is not
    /// retained after this call returns.</summary>
    public async Task<bool> SignInAsync(string email, string password)
    {
        if (Busy) return false;
        Busy = true;
        Error = null;
        try
        {
            var e = email.Trim();
            if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(password))
            {
                Error = "Enter email and password.";
                return false;
            }
            await _auth.LoginAsync(e, password);
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

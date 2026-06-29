using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>
/// Login form state. Mirrors the original's "use credentials from env" toggle:
/// when checked, reads NUGS_EMAIL / NUGS_PASSWORD; otherwise uses the typed fields.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly NugsAuth _auth;

    [ObservableProperty] private bool useEnv = true;
    [ObservableProperty] private string email = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string? error;

    public LoginViewModel(NugsAuth auth) => _auth = auth;

    /// <summary>Attempts sign-in. Returns true on success.</summary>
    public async Task<bool> SignInAsync()
    {
        if (Busy) return false;
        Busy = true;
        Error = null;
        try
        {
            var e = UseEnv ? Environment.GetEnvironmentVariable("NUGS_EMAIL") : Email;
            var p = UseEnv ? Environment.GetEnvironmentVariable("NUGS_PASSWORD") : Password;
            if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p))
            {
                Error = "missing credentials";
                return false;
            }
            await _auth.LoginAsync(e!, p!);
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            Busy = false;
        }
    }
}

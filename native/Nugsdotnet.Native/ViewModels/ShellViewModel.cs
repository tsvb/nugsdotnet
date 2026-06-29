using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Top-level state: whether we're signed in, and the plan label to show.</summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly NugsAuth _auth;

    [ObservableProperty] private bool isLoggedIn;
    [ObservableProperty] private string? planLabel;

    public ShellViewModel(NugsAuth auth) => _auth = auth;

    public async Task InitializeAsync()
    {
        var info = await _auth.GetSessionInfoAsync();
        IsLoggedIn = info.LoggedIn;
        PlanLabel = info.Plan;
    }

    public async Task SignOutAsync()
    {
        await _auth.LogoutAsync();
        IsLoggedIn = false;
        PlanLabel = null;
    }
}

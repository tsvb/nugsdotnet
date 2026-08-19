using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Top-level state: whether we're signed in, and the plan label to show.</summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly NugsAuth _auth;
    private readonly AccountLocalStore _accounts;

    [ObservableProperty] private bool isLoggedIn;
    [ObservableProperty] private string? planLabel;

    public ShellViewModel(NugsAuth auth, AccountLocalStore accounts)
    {
        _auth = auth;
        _accounts = accounts;
    }

    public async Task InitializeAsync()
    {
        var info = await _auth.GetSessionInfoAsync();
        IsLoggedIn = info.LoggedIn;
        PlanLabel = info.Plan;
        if (info is { LoggedIn: true, UserId: { Length: > 0 } id })
            _accounts.Bind(id);
        else
            _accounts.Unbind();
    }

    public async Task SignOutAsync()
    {
        await _auth.LogoutAsync();
        _accounts.Unbind();
        IsLoggedIn = false;
        PlanLabel = null;
    }
}

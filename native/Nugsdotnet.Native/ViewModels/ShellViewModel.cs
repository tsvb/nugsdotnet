using CommunityToolkit.Mvvm.ComponentModel;
using Nugsdotnet.Native.Core;
using Nugsdotnet.Native.Playback;

namespace Nugsdotnet.Native.ViewModels;

/// <summary>Top-level state: whether we're signed in, and the plan label to show.</summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly NugsAuth _auth;
    private readonly AccountLocalStore _accounts;
    private readonly JournalTracker _journal;

    [ObservableProperty] public partial bool IsLoggedIn { get; set; }
    [ObservableProperty] public partial string? PlanLabel { get; set; }
    [ObservableProperty] public partial bool ContentAccessible { get; set; } = true;
    [ObservableProperty] public partial string? SubscriptionWarning { get; set; }

    public ShellViewModel(NugsAuth auth, AccountLocalStore accounts, JournalTracker journal)
    {
        _auth = auth;
        _accounts = accounts;
        _journal = journal;
    }

    public async Task InitializeAsync()
    {
        var info = await _auth.GetSessionInfoAsync();
        IsLoggedIn = info.LoggedIn;
        PlanLabel = info.Plan;
        ContentAccessible = info.Accessible;
        SubscriptionWarning = info is { LoggedIn: true, Accessible: false }
            ? "Your nugs subscription may have expired — renew at nugs.net to stream."
            : null;
        if (info is { LoggedIn: true, UserId: { Length: > 0 } id })
        {
            _accounts.Bind(id);
            await _journal.StartAsync();
        }
        else
        {
            _accounts.Unbind();
        }
    }

    public async Task SignOutAsync()
    {
        await _journal.StopAsync();
        await _auth.LogoutAsync();
        _accounts.Unbind();
        IsLoggedIn = false;
        PlanLabel = null;
        ContentAccessible = true;
        SubscriptionWarning = null;
    }
}

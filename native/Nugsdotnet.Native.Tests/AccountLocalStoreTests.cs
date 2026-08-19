using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class AccountLocalStoreTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "nugsdotnet-tests", Path.GetRandomFileName());

    private static StashEntry Entry(string id) => new(
        id, $"Show {id}", "Artist", "6/24/2023", "Venue", "/images/x.jpg",
        DateTimeOffset.UtcNow);

    private static RecentPlay Play(string id) => new(
        id, $"Show {id}", "Artist", "6/24/2023", "Venue", "/images/x.jpg",
        DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("user_1")]
    [InlineData("abc.def")]
    public void SanitizeUserId_keeps_safe_segments(string id)
        => Assert.Equal(id, NugsLocalPaths.SanitizeUserId(id));

    [Theory]
    [InlineData("../escape")]
    [InlineData("alice/bob")]
    [InlineData("user id")]
    [InlineData("alice\\bob")]
    public void SanitizeUserId_hashes_unsafe_segments(string id)
    {
        var safe = NugsLocalPaths.SanitizeUserId(id);
        Assert.Matches("^[0-9a-f]{64}$", safe);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, safe);
        Assert.DoesNotContain("..", safe);
        Assert.Equal(safe, NugsLocalPaths.SanitizeUserId(id));   // stable
    }

    [Fact]
    public void SanitizeUserId_rejects_blank()
        => Assert.Throws<ArgumentException>(() => NugsLocalPaths.SanitizeUserId("  "));

    [Fact]
    public async Task Stash_and_recents_are_isolated_per_nugs_account()
    {
        var accounts = new AccountLocalStore(TempRoot());
        var stash = new StashStore(accounts);
        var recents = new RecentsStore(accounts);

        Assert.Empty(await stash.LoadAsync());   // unbound
        Assert.False(await stash.ToggleAsync(Entry("nope")));

        accounts.Bind("alice");
        Assert.True(await stash.ToggleAsync(Entry("a")));
        await recents.RecordAsync(Play("ra"));

        accounts.Bind("bob");
        Assert.Empty(await stash.LoadAsync());
        Assert.Empty(await recents.LoadAsync());
        Assert.True(await stash.ToggleAsync(Entry("b")));

        accounts.Bind("alice");
        Assert.Equal(new[] { "a" }, (await stash.LoadAsync()).Select(e => e.ContainerId));
        Assert.Equal(new[] { "ra" }, (await recents.LoadAsync()).Select(p => p.ContainerId));
        Assert.DoesNotContain((await stash.LoadAsync()), e => e.ContainerId == "b");

        accounts.Unbind();
        Assert.Empty(await stash.LoadAsync());
        Assert.Empty(await recents.LoadAsync());
    }

    [Fact]
    public async Task Playback_is_isolated_per_nugs_account()
    {
        var accounts = new AccountLocalStore(TempRoot());
        var store = new PlaybackStateStore(accounts);
        var snap = new PlaybackSnapshot(
            new[] { new NowPlaying("t1", "Opener", "Goose", "Show", null, "c1") },
            0, 12, 0.5, false);

        Assert.Null(await store.LoadAsync());
        await store.SaveAsync(snap);             // unbound — no-op
        Assert.Null(await store.LoadAsync());

        accounts.Bind("alice");
        await store.SaveAsync(snap);
        Assert.Equal("t1", (await store.LoadAsync())!.Queue[0].TrackId);

        accounts.Bind("bob");
        Assert.Null(await store.LoadAsync());

        accounts.Bind("alice");
        Assert.Equal("t1", (await store.LoadAsync())!.Queue[0].TrackId);
    }

    [Fact]
    public async Task First_bind_migrates_legacy_profile_root_files()
    {
        var root = TempRoot();
        Directory.CreateDirectory(root);
        var legacy = Path.Combine(root, NugsLocalPaths.StashFileName);
        await File.WriteAllTextAsync(legacy, """[{"ContainerId":"legacy"}]""");

        var accounts = new AccountLocalStore(root);
        var stash = new StashStore(accounts);

        accounts.Bind("alice");
        Assert.False(File.Exists(legacy));
        Assert.Equal("legacy", (await stash.LoadAsync()).Single().ContainerId);

        accounts.Bind("bob");
        Assert.Empty(await stash.LoadAsync());   // bob does not inherit alice's migrated stash
    }

    [Fact]
    public void Bind_puts_files_under_accounts_userId()
    {
        var root = TempRoot();
        var accounts = new AccountLocalStore(root);
        accounts.Bind("alice");
        var expected = Path.Combine(root, NugsLocalPaths.AccountsFolder, "alice", NugsLocalPaths.StashFileName);
        Assert.Equal(expected, accounts.File(NugsLocalPaths.StashFileName));
    }
}

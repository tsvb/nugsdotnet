using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class SessionStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "nugsdotnet-tests", Path.GetRandomFileName(), "session.bin");

    private static PersistedSession Sample() => new(
        new TokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        "user-1",
        new SubInfo("sub-1", "01/01/2020 00:00:00", "01/01/2030 00:00:00", true,
            new SubInfo.PlanInfo("p1", "VIP"), null));

    [Fact]
    public async Task Save_Load_roundtrips_and_Clear_removes_the_file()
    {
        var path = TempPath();
        var store = new NugsSessionStore(path);
        await store.SaveAsync(Sample());

        var loaded = await store.LoadAsync();
        Assert.Equal("access", loaded!.Tokens.AccessToken);
        Assert.Equal("user-1", loaded.UserId);
        Assert.Equal("VIP", Session.From(loaded).PlanDescription);

        await store.ClearAsync();
        Assert.False(File.Exists(path));
        Assert.Null(await new NugsSessionStore(path).LoadAsync());
    }

    [Fact]
    public async Task Load_returns_null_for_corrupt_blob()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "not-a-session");
        Assert.Null(await new NugsSessionStore(path).LoadAsync());
    }
}

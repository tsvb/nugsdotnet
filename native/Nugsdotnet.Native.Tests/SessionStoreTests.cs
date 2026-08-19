using System.Text.Json;
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

    [Fact]
    public async Task Load_upgrades_a_legacy_unprefixed_blob_without_dropping_the_session()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(Sample());
        await File.WriteAllBytesAsync(path, NugsSessionStore.EncryptLegacy(json));

        var loaded = await new NugsSessionStore(path).LoadAsync();
        Assert.Equal("access", loaded!.Tokens.AccessToken);
        Assert.Equal("user-1", loaded.UserId);

        var onDisk = await File.ReadAllBytesAsync(path);
        Assert.True(onDisk.AsSpan().StartsWith(NugsSessionStore.V1Prefix));

        var reloaded = await new NugsSessionStore(path).LoadAsync();
        Assert.Equal("refresh", reloaded!.Tokens.RefreshToken);
    }

    [Fact]
    public void Encrypt_roundtrip_through_TryDecrypt_is_v1()
    {
        var json = "{\"ok\":true}"u8.ToArray();
        var blob = NugsSessionStore.Encrypt(json);
        Assert.True(blob.AsSpan().StartsWith(NugsSessionStore.V1Prefix));
        Assert.True(NugsSessionStore.TryDecrypt(blob, out var plain, out var legacy));
        Assert.False(legacy);
        Assert.Equal(json, plain);
    }
}

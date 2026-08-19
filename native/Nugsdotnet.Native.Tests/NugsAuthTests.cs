using System.Net;
using System.Net.Http;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class NugsAuthTests
{
    private static string TempSession() =>
        Path.Combine(Path.GetTempPath(), "nugsdotnet-tests", Path.GetRandomFileName(), "session.bin");

    [Fact]
    public void ExpiresAt_skews_by_a_minute_for_hour_long_tokens()
    {
        var before = DateTimeOffset.UtcNow;
        var at = NugsAuth.ExpiresAt(3600);
        var after = DateTimeOffset.UtcNow;
        Assert.InRange(at, before.AddSeconds(3539), after.AddSeconds(3541));
    }

    [Fact]
    public void ExpiresAt_does_not_treat_short_lived_tokens_as_already_expired()
    {
        var at = NugsAuth.ExpiresAt(10);
        Assert.True(at > DateTimeOffset.UtcNow);   // 10s lifetime, 5s skew
    }

    [Fact]
    public async Task Login_failure_does_not_echo_the_idp_body()
    {
        var http = new HttpClient(new MapHandler
        {
            Routes =
            {
                [NugsConstants.AuthUrl] = _ => Json(HttpStatusCode.BadRequest,
                    """{"error":"invalid_grant","error_description":"password is hunter2"}"""),
            },
        });
        var auth = new NugsAuth(http, new NugsSessionStore(TempSession()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.LoginAsync("you@example.com", "hunter2"));

        Assert.Contains("sign-in failed", ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
        Assert.DoesNotContain("invalid_grant", ex.Message);
    }

    [Fact]
    public void Auth_endpoints_are_public_https()
    {
        Assert.True(NugsUri.IsSafeHttps(NugsConstants.AuthUrl, out var token));
        Assert.Equal(Uri.UriSchemeHttps, token!.Scheme);
        Assert.True(NugsUri.IsSafeHttps(NugsConstants.UserInfoUrl, out _));
        Assert.True(NugsUri.IsSafeHttps(NugsConstants.SubInfoUrl, out _));
    }

    [Fact]
    public async Task Login_posts_the_password_grant_over_https()
    {
        Uri? posted = null;
        var http = new HttpClient(new MapHandler
        {
            Routes =
            {
                [NugsConstants.AuthUrl] = req =>
                {
                    posted = req.RequestUri;
                    return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
                },
            },
        });
        var auth = new NugsAuth(http, new NugsSessionStore(TempSession()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.LoginAsync("you@example.com", "hunter2"));

        Assert.NotNull(posted);
        Assert.Equal(Uri.UriSchemeHttps, posted!.Scheme);
        Assert.Equal("id.nugs.net", posted.IdnHost);
        Assert.Equal("/connect/token", posted.AbsolutePath);
    }

    [Fact]
    public async Task Login_then_refresh_keeps_the_refresh_token_when_the_idp_omits_it()
    {
        var tokenCalls = 0;
        var http = new HttpClient(new MapHandler
        {
            Routes =
            {
                [NugsConstants.AuthUrl] = _ =>
                {
                    tokenCalls++;
                    return tokenCalls == 1
                        ? Json(HttpStatusCode.OK,
                            """{"access_token":"a1","refresh_token":"r1","expires_in":0}""")
                        : Json(HttpStatusCode.OK,
                            """{"access_token":"a2","expires_in":3600}""");
                },
                [NugsConstants.UserInfoUrl] = _ => Json(HttpStatusCode.OK, """{"sub":"user-1"}"""),
                [NugsConstants.SubInfoUrl] = _ => Json(HttpStatusCode.OK, """
                    {
                      "legacySubscriptionId": "sub-1",
                      "startedAt": "01/01/2020 00:00:00",
                      "endsAt": "01/01/2030 00:00:00",
                      "isContentAccessible": true,
                      "plan": { "id": "p1", "description": "VIP" }
                    }
                    """),
            },
        });
        var store = new NugsSessionStore(TempSession());
        var auth = new NugsAuth(http, store);

        await auth.LoginAsync("you@example.com", "pw");
        var session = await auth.GetSessionAsync();

        Assert.Equal("a2", session.AccessToken);
        var persisted = await store.LoadAsync();
        Assert.Equal("r1", persisted!.Tokens.RefreshToken);
        Assert.Equal(2, tokenCalls);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class MapHandler : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> Routes { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{request.RequestUri!.Scheme}://{request.RequestUri.Authority}{request.RequestUri.AbsolutePath}";
            if (!Routes.TryGetValue(key, out var reply))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(reply(request));
        }
    }
}

using System.Net;
using System.Net.Http;
using Nugsdotnet.Native.Core;

namespace Nugsdotnet.Native.Tests;

public class BoundedContentTests
{
    [Fact]
    public async Task ReadStringAsync_returns_a_small_body()
    {
        using var res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("hello"),
        };
        Assert.Equal("hello", await BoundedContent.ReadStringAsync(res, 64, CancellationToken.None));
    }

    [Fact]
    public async Task ReadStringAsync_rejects_a_declared_oversize_body()
    {
        using var res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[8]) { Headers = { ContentLength = 99 } },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BoundedContent.ReadStringAsync(res, 16, CancellationToken.None));
    }

    [Fact]
    public async Task ReadStringAsync_rejects_a_body_that_exceeds_the_cap()
    {
        using var res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 32)),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BoundedContent.ReadStringAsync(res, 16, CancellationToken.None));
    }
}

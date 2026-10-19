using System.Net;
using AtomUpdate.Core.Constants;

namespace AtomUpdate.Tests.Api;

/// <summary>响应压缩：HTTP 明文 + Accept-Encoding 时 JSON 响应走 gzip（EnableForHttps=false，HTTPS 由反代处理）。</summary>
[Collection("api")]
public class ResponseCompressionTests(ApiFixture fixture)
{
    [Fact]
    public async Task Json_Response_IsGzipCompressed_WhenClientAccepts()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Health);
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
    }

    [Fact]
    public async Task Json_Response_IsUncompressed_WithoutAcceptEncoding()
    {
        using var response = await fixture.Client.GetAsync(ApiRoutes.Health);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}

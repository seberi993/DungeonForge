using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Infrastructure;

public class CorsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }

    [Theory]
    [InlineData("http://localhost:5200")]
    [InlineData("https://localhost:7200")]
    public async Task Preflight_FromAllowedOrigin_ReturnsAllowOriginHeader(string origin)
    {
        var response = await factory.CreateClient().SendAsync(Preflight(origin));

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be(origin);
    }

    [Theory]
    [InlineData("http://localhost:5200")]
    [InlineData("https://localhost:7200")]
    public async Task Preflight_FromAllowedOrigin_AllowsCredentials(string origin)
    {
        var response = await factory.CreateClient().SendAsync(Preflight(origin));

        response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task Preflight_FromDisallowedOrigin_OmitsAllowOriginHeader()
    {
        var response = await factory.CreateClient().SendAsync(Preflight("https://evil.example.com"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}

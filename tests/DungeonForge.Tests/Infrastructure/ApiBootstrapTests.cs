using System.Net;
using DungeonForge.Api.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Infrastructure;

public class ApiBootstrapTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void MemoryCache_IsRegistered()
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetService<IMemoryCache>().Should().NotBeNull();
    }

    [Fact]
    public void Endpoints_AreRegistered()
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetServices<IEndpoint>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithCamelCaseJson()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"status":"ok"}""");
    }
}

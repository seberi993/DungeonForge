using FluentAssertions;
using DungeonForge.Application.Narration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DungeonForge.Tests.Infrastructure.Narration;

public sealed class NarrationConfigTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void AppSettings_ConfigureFlashLiteModelAndEightSecondTimeout()
    {
        var options = factory.Services.GetRequiredService<IOptions<NarrationOptions>>().Value;

        options.Model.Should().Be("gemini-flash-lite-latest");
        options.TimeoutSeconds.Should().Be(8);
    }
}

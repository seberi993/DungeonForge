using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Application.Errors;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class UnlockTurnErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [CoversError(ErrorCodes.Party.NotFound)]
    public async Task UnknownParty_Returns404Problem_NamingThePartyAndTheOperation()
    {
        var id = Guid.NewGuid();

        var response = await factory.CreateClient().PostAsJsonAsync($"/api/parties/{id}/unlock", new { memberId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(404);
        root.GetProperty("code").GetString().Should().Be("party.not_found");
        root.GetProperty("operation").GetString().Should().Be("Command:UnlockTurn");
        root.GetProperty("parameters").GetProperty("partyId").GetString().Should().Be(id.ToString());
        root.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }
}

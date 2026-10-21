using System.Net;
using DungeonForge.Application.Errors;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class GetPartyStateErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [CoversError(ErrorCodes.Party.NotFound)]
    public async Task UnknownParty_Returns404Problem()
    {
        var id = Guid.NewGuid();

        using var problem = await ProblemBody.ReadAsync(
            await factory.CreateClient().GetAsync($"/api/parties/{id}"), HttpStatusCode.NotFound);

        problem.Code.Should().Be("party.not_found");
        problem.Operation.Should().Be("Query:GetPartyState");
        problem.Parameter("partyId").Should().Be(id.ToString());
    }
}

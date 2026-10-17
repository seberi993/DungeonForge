using System.Net;
using DungeonForge.Api.Errors;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class JoinByCodeErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("ZZZZ-ZZZZ")]
    [InlineData("short")]
    [CoversError(ErrorCodes.Party.InviteNotFound)]
    public async Task UnknownOrMalformedCode_Returns404Problem(string code)
    {
        using var problem = await ProblemBody.ReadAsync(
            await factory.CreateClient().GetAsync($"/api/parties/by-code/{code}"), HttpStatusCode.NotFound);

        problem.Code.Should().Be("party.invite_not_found");
        problem.Operation.Should().Be("Query:JoinByCode");
        problem.Parameter("code").Should().Be(code);
    }
}

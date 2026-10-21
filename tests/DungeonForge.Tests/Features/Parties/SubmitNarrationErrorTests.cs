using System.Net;
using System.Net.Http.Json;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.SubmitNarration;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class SubmitNarrationErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Operation = "Command:SubmitNarration";

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid id, int turn, string? text = null) =>
        client.PostAsJsonAsync($"/api/parties/{id}/narration", new { turn, text });

    [Fact]
    public async Task UnknownParty_Returns404Problem()
    {
        var id = Guid.NewGuid();

        using var problem = await ProblemBody.ReadAsync(await SubmitAsync(factory.CreateClient(), id, 0, "x"), HttpStatusCode.NotFound);

        problem.Code.Should().Be("party.not_found");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("partyId").Should().Be(id.ToString());
    }

    [Fact]
    [CoversError(ErrorCodes.Narration.NotAwaited)]
    public async Task WhenNothingIsAwaited_Returns409Problem()
    {
        var client = factory.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var problem = await ProblemBody.ReadAsync(await SubmitAsync(client, id, 0, "A goblin waits."), HttpStatusCode.Conflict);

        problem.Code.Should().Be("narration.not_awaited");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("partyId").Should().Be(id.ToString());
        problem.Parameter("turn").Should().Be("0");
    }

    [Fact]
    [CoversError(ErrorCodes.Narration.TextTooLong)]
    public async Task TextOverTheLimit_Returns400Problem_WithTheMessageUnderText()
    {
        var client = factory.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await SubmitAsync(client, id, 0, new string('x', SubmitNarrationLimits.MaxTextLength + 1));

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.BadRequest);
        problem.Code.Should().Be("narration.text_too_long");
        problem.Operation.Should().Be(Operation);
        problem.Root.GetProperty("errors").GetProperty("text").EnumerateArray().Single().GetString()
            .Should().Be($"Text must be at most {SubmitNarrationLimits.MaxTextLength} characters.");
    }
}

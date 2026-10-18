using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Errors;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class SetupTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<Guid> CreatePaintedPartyAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync(
            "/api/parties", new { name = "Heroes", members = PartyTestData.DefaultMembers, paintScene = true });
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("partyId").GetGuid();
    }

    private static Task<HttpResponseMessage> AnswerAsync(HttpClient client, Guid id, string player, string text = "A flooded crypt.") =>
        client.PostAsJsonAsync($"/api/parties/{id}/setup", new { playerName = player, text });

    [Fact]
    public async Task EachPlayer_GetsADifferentQuestion()
    {
        var client = factory.CreateClient();
        var id = await CreatePaintedPartyAsync(client);

        var first = await client.GetFromJsonAsync<JsonElement>($"/api/parties/{id}/setup?playerName=Conan");
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/parties/{id}/setup?playerName=Merlin");

        first.GetProperty("question").GetString().Should().NotBe(second.GetProperty("question").GetString());
        first.GetProperty("pending").GetBoolean().Should().BeTrue();
        first.GetProperty("total").GetInt32().Should().Be(3);
    }

    [Fact]
    [CoversError(ErrorCodes.Setup.Pending)]
    public async Task Explore_IsBlocked_UntilEveryoneAnswers()
    {
        var client = factory.CreateClient();
        var id = await CreatePaintedPartyAsync(client);
        (await AnswerAsync(client, id, "Conan")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnswerAsync(client, id, "Merlin")).StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await client.PostAsync($"/api/parties/{id}/explore", null);

        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = await ProblemBody.ReadAsync(blocked, HttpStatusCode.Conflict);
        problem.Code.Should().Be("setup.pending");
        problem.Operation.Should().Be("Query:GetScenario");
        problem.Parameter("partyId").Should().Be(id.ToString());
    }

    [Fact]
    public async Task LastAnswer_ClearsPending_AndStateReportsProgress()
    {
        var client = factory.CreateClient();
        var id = await CreatePaintedPartyAsync(client);
        await AnswerAsync(client, id, "Conan");
        await AnswerAsync(client, id, "Merlin");

        var last = await AnswerAsync(client, id, "Mercy");

        last.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await last.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pending").GetBoolean().Should().BeFalse();
        var state = await client.GetFromJsonAsync<JsonElement>($"/api/parties/{id}");
        state.GetProperty("setupAnswered").GetInt32().Should().Be(3);
        state.GetProperty("setupPending").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ForceStart_ReleasesTheGate_WithoutEveryAnswer()
    {
        var client = factory.CreateClient();
        var id = await CreatePaintedPartyAsync(client);

        var forced = await client.PostAsJsonAsync($"/api/parties/{id}/setup", new { playerName = "Conan", text = "A burning inn.", forceStart = true });

        (await forced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pending").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AnsweredPlayer_CanForceStart_WithoutTheStragglers()
    {
        var client = factory.CreateClient();
        var id = await CreatePaintedPartyAsync(client);
        await AnswerAsync(client, id, "Conan");

        var forced = await client.PostAsJsonAsync($"/api/parties/{id}/setup", new { playerName = "Conan", text = "(begins the tale)", forceStart = true });

        forced.StatusCode.Should().Be(HttpStatusCode.OK);
        (await forced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pending").GetBoolean().Should().BeFalse();
    }

    [Fact]
    [CoversError(ErrorCodes.Setup.AlreadyAnswered)]
    public async Task AnsweringTwice_Returns409()
    {
        var client = factory.CreateClient();
        var id = await CreatePaintedPartyAsync(client);
        await AnswerAsync(client, id, "Conan");

        var again = await AnswerAsync(client, id, "Conan");

        using var problem = await ProblemBody.ReadAsync(again, HttpStatusCode.Conflict);
        problem.Code.Should().Be("setup.already_answered");
        problem.Operation.Should().Be("Command:SubmitSetup");
    }

    [Fact]
    [CoversError(ErrorCodes.Setup.NotInSetup)]
    public async Task PartyWithoutSetup_RejectsAnswers()
    {
        var client = factory.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await AnswerAsync(client, id, "Conan");

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.Conflict);
        problem.Code.Should().Be("setup.not_in_setup");
    }

    [Fact]
    public async Task EmptyText_Returns400_WithTextError()
    {
        var client = factory.CreateClient();
        var id = await CreatePaintedPartyAsync(client);

        var response = await AnswerAsync(client, id, "Conan", "  ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PartyWithoutSetup_StartsAtOnce()
    {
        var client = factory.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        var state = await client.GetFromJsonAsync<JsonElement>($"/api/parties/{id}");

        state.GetProperty("setupPending").GetBoolean().Should().BeFalse();
    }
}

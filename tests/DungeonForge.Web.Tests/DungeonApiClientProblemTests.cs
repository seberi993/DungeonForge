using System.Net;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

// The API answers failures with ProblemDetails carrying code, detail and parameters. The client exposes them and, where a
// code is present, decides from it; bodies without one (older servers, the waiting 409) keep the old shape-based rules.
public class DungeonApiClientProblemTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string Problem(int status, string code, string detail, string? parameters = null, string? errors = null) =>
        $$"""{"type":"https://example.test/p","title":"t","status":{{status}},"detail":"{{detail}}","code":"{{code}}","operation":"Query:Test","parameters":{{parameters ?? "{}"}},"traceId":"abc"{{(errors is null ? "" : ",\"errors\":" + errors)}}}""";

    private static Task<ApiResult<ExploreResponse>> ExploreAsync(HttpStatusCode status, string body) =>
        new DungeonApiClient(new FakeHandler(status, body).CreateClient()).ExploreAsync(Id);

    [Fact]
    public async Task Conflict_WithACode_IsConflict_NeverWaiting_AndCarriesTheProblem()
    {
        var result = await ExploreAsync(HttpStatusCode.Conflict,
            Problem(409, "turn.party_defeated", "Every member is defeated; the run is over.", $$"""{"partyId":"{{Id}}","turn":"3"}"""));

        result.Status.Should().Be(ApiStatus.Conflict);
        result.Code.Should().Be("turn.party_defeated");
        result.Description.Should().Be("Every member is defeated; the run is over.");
        result.Parameters.Should().Contain("partyId", Id.ToString()).And.Contain("turn", "3");
    }

    [Fact]
    public async Task Conflict_WaitingBody_IsStillWaiting_WithNoCode()
    {
        var result = await ExploreAsync(HttpStatusCode.Conflict,
            $$"""{"partyId":"{{Id}}","locked":1,"required":3,"lockStatus":[],"status":"waiting"}""");

        result.Status.Should().Be(ApiStatus.Waiting);
        result.Code.Should().BeNull();
        result.Parameters.Should().BeNull();
    }

    [Fact]
    public async Task Conflict_WithoutACode_AndNoWaitingMarker_IsConflict()
    {
        (await ExploreAsync(HttpStatusCode.Conflict, "")).Status.Should().Be(ApiStatus.Conflict);
        (await ExploreAsync(HttpStatusCode.Conflict, """{"status":409}""")).Status.Should().Be(ApiStatus.Conflict);
    }

    [Fact]
    public async Task NotFound_CarriesTheCodeAndParameters()
    {
        var result = await ExploreAsync(HttpStatusCode.NotFound,
            Problem(404, "party.not_found", "No party with that id exists, or it has expired.", $$"""{"partyId":"{{Id}}"}"""));

        result.Status.Should().Be(ApiStatus.NotFound);
        result.Code.Should().Be("party.not_found");
        result.Parameters.Should().Contain("partyId", Id.ToString());
    }

    [Fact]
    public async Task BadRequest_KeepsTheErrorsMessage_AndAlsoExposesTheCode()
    {
        var result = await ExploreAsync(HttpStatusCode.BadRequest,
            Problem(400, "turn.no_active_encounter", "There is no active encounter to resolve.",
                errors: """{"actions":["There is no active encounter to resolve."]}"""));

        result.Status.Should().Be(ApiStatus.ValidationError);
        result.Error.Should().Be("There is no active encounter to resolve.");
        result.Code.Should().Be("turn.no_active_encounter");
    }

    [Fact]
    public async Task BadRequest_WithoutErrors_FallsBackToTheDescription()
    {
        var result = await ExploreAsync(HttpStatusCode.BadRequest, Problem(400, "request.malformed", "The body is not valid JSON."));

        result.Error.Should().Be("The body is not valid JSON.");
    }

    [Fact]
    public async Task BadRequest_WithNoUsableBody_UsesTheGenericMessage()
    {
        (await ExploreAsync(HttpStatusCode.BadRequest, "not json")).Error.Should().Be("The request was invalid.");
    }

    [Fact]
    public async Task ServerError_IsFailed_ButStillExposesTheCode()
    {
        var result = await ExploreAsync(HttpStatusCode.InternalServerError,
            Problem(500, "unexpected.unhandled", "The server failed to process the request."));

        result.Status.Should().Be(ApiStatus.Failed);
        result.Error.Should().Be("Server returned 500.");
        result.Code.Should().Be("unexpected.unhandled");
        result.Description.Should().Be("The server failed to process the request.");
    }

    [Fact]
    public async Task Success_HasNoProblemFields()
    {
        var result = await ExploreAsync(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"x","gold":0,"isPartyDefeated":false}""");

        result.Code.Should().BeNull();
        result.Description.Should().BeNull();
        result.Parameters.Should().BeNull();
    }
}

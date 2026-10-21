using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Social;

public class PeerSliceTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<(string PromptId, string About, string Question)> PromptAsync(SocialHarness h, Guid id, string rater)
    {
        var response = await h.Client.GetAsync($"/api/parties/{id}/social/peer-prompt?playerName={rater}");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return (root.GetProperty("promptId").GetString()!, root.GetProperty("about").GetString()!, root.GetProperty("question").GetString()!);
    }

    private static Task<HttpResponseMessage> AnswerAsync(SocialHarness h, Guid id, string rater, string promptId, string choice) =>
        h.Client.PostAsJsonAsync($"/api/parties/{id}/social/peer-answer", new { playerName = rater, promptId, choice });

    [Fact]
    public async Task Prompt_IsAboutATeammate_NamesThemAndIsStableUntilAnswered()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();

        var first = await PromptAsync(h, id, "Conan");
        var again = await PromptAsync(h, id, "Conan");

        first.About.Should().NotBe("Conan").And.BeOneOf("Merlin", "Lyra", "Dru");
        first.Question.Should().Contain(first.About);
        again.Should().Be(first);
    }

    [Fact]
    public async Task Answering_AdvancesToANewPrompt_UntilTheAllowanceIsSpent()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();

        for (var i = 0; i < 3; i++)
        {
            var prompt = await PromptAsync(h, id, "Conan");
            (await AnswerAsync(h, id, "Conan", prompt.PromptId, "yes")).EnsureSuccessStatusCode();
        }

        using var problem = await ProblemBody.ReadAsync(
            await h.Client.GetAsync($"/api/parties/{id}/social/peer-prompt?playerName=Conan"), HttpStatusCode.Conflict);
        problem.Code.Should().Be("social.no_peer_prompt");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.NoPeerPrompt)]
    public async Task Prompt_WithNoOneElseToRate_IsAConflict()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync([new { name = "Solo", @class = "warrior" }]);

        using var problem = await ProblemBody.ReadAsync(
            await h.Client.GetAsync($"/api/parties/{id}/social/peer-prompt?playerName=Solo"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("social.no_peer_prompt");
        problem.Operation.Should().Be("Query:GetPeerPrompt");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.PeerPromptUnknown)]
    public async Task Answer_ForAPromptThatIsNotYours_IsNotFound_AndAnAnswerCannotBeRepeated()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var prompt = await PromptAsync(h, id, "Conan");

        using var wrong = await ProblemBody.ReadAsync(await AnswerAsync(h, id, "Merlin", prompt.PromptId, "yes"), HttpStatusCode.NotFound);
        (await AnswerAsync(h, id, "Conan", prompt.PromptId, "yes")).EnsureSuccessStatusCode();
        using var repeat = await ProblemBody.ReadAsync(await AnswerAsync(h, id, "Conan", prompt.PromptId, "yes"), HttpStatusCode.NotFound);

        wrong.Code.Should().Be("social.peer_prompt_unknown");
        repeat.Code.Should().Be("social.peer_prompt_unknown");
    }

    [Fact]
    public async Task ThreeAgreeingTeammates_ShiftTheSubjectInSecret_AndTellOnlyThem()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var first = await PromptAsync(h, id, "Conan");
        var subject = first.About;
        var raters = SocialHarness.Four.Where(n => n != subject).ToArray();

        foreach (var rater in raters)
        {
            var prompt = await PromptAsync(h, id, rater);
            prompt.PromptId.Should().Be(first.PromptId, "raters join the round somebody already started");
            var answered = await AnswerAsync(h, id, rater, prompt.PromptId, "yes");
            answered.EnsureSuccessStatusCode();
            (await answered.Content.ReadAsStringAsync()).Should().Be("{\"accepted\":true}", "an answer reveals nothing about the effect");
        }

        h.Profile(id, subject).Axes.ToDictionary().Values.Should().Contain(v => v != 0);
        h.PrivateTexts(subject, "narrator").Should().Contain("All eyes agree on you. Something stirs.");
        foreach (var other in SocialHarness.Four.Where(n => n != subject))
        {
            h.PrivateTexts(other, "narrator").Should().NotContain("All eyes agree on you. Something stirs.");
        }

        h.Notifier.PublicTraffic.Should().NotContain("Something stirs");
    }

    [Fact]
    public async Task APeerRound_NeverMovesAnAxisBeyondTheCap()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync([new { name = "Conan", @class = "warrior" }, new { name = "Merlin", @class = "mage" }]);

        // Two members: Conan keeps rating Merlin. The allowance (3 + floors) bounds how many rounds there can be.
        for (var i = 0; i < 6; i++)
        {
            var response = await h.Client.GetAsync($"/api/parties/{id}/social/peer-prompt?playerName=Conan");
            if (response.StatusCode != HttpStatusCode.OK) break;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            (await AnswerAsync(h, id, "Conan", doc.RootElement.GetProperty("promptId").GetString()!, "yes")).EnsureSuccessStatusCode();
        }

        h.Profile(id, "Merlin").PeerShift.ToDictionary().Values.Should().OnlyContain(v => Math.Abs(v) <= 40);
    }

    [Fact]
    public async Task PeersModuleOff_AnswersNotFound()
    {
        using var h = SocialHarness.Create(factory, new() { ["Social:Peers"] = "false" });
        var id = await h.PartyAsync();

        using var problem = await ProblemBody.ReadAsync(
            await h.Client.GetAsync($"/api/parties/{id}/social/peer-prompt?playerName=Conan"), HttpStatusCode.NotFound);

        problem.Code.Should().Be("social.module_disabled");
    }
}

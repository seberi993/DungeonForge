using System.Net;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Social;

public class CompelRevealTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Compelling_CostsACredibilityPoint_AndTellsTheRecipientTheyWerePressed()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();

        (await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?", compel: true)).EnsureSuccessStatusCode();

        h.Profile(id, "Conan").Credibility.Should().Be(PlayerProfile.StartingCredibility - 1);
        h.PrivateTexts("Merlin", PrivateKinds.Ask).Should().ContainSingle().Which.Should().Contain("Someone pressed you");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.NotEnoughCredibility)]
    public async Task Compelling_WithAWorthlessWord_IsRefused_AndSpendsNothing()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        h.Profile(id, "Conan").Credibility = 0;

        using var problem = await ProblemBody.ReadAsync(
            await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?", compel: true), HttpStatusCode.Conflict);

        problem.Code.Should().Be("social.not_enough_credibility");
        h.Profile(id, "Conan").AskTokens.Should().Be(PlayerProfile.StartingTokens);
    }

    [Fact]
    [CoversError(ErrorCodes.Social.BluffForbidden)]
    public async Task APressedRecipient_CannotBluff_ButCanBeHonestOrDodge()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin", "Lyra"], "Who has the key?", compel: true));

        using var problem = await ProblemBody.ReadAsync(await h.ReplyAsync(id, ask, "Merlin", "bluff", "Nobody"), HttpStatusCode.BadRequest);
        (await h.ReplyAsync(id, ask, "Merlin", "honest", "Dru")).EnsureSuccessStatusCode();
        (await h.ReplyAsync(id, ask, "Lyra", "dodge")).EnsureSuccessStatusCode();

        problem.Code.Should().Be("social.bluff_forbidden");
        problem.Root.GetProperty("errors").GetProperty("choice").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Reveal_ShowsTheExchangeToTheWholeParty_WithoutAnyBluffLabel_AndCostsCredibility()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin", "Lyra"], "Who has the key?"));
        (await h.ReplyAsync(id, ask, "Merlin", "bluff", "Dru has it")).EnsureSuccessStatusCode();
        (await h.ReplyAsync(id, ask, "Lyra", "dodge")).EnsureSuccessStatusCode();

        (await h.RevealAsync(id, ask, "Conan")).EnsureSuccessStatusCode();

        var public_ = h.Notifier.Events.Single(e => e.Kind == "narrator" && e.Text.Contains("lays a whisper bare")).Text;
        public_.Should().Contain("Who has the key?").And.Contain("Merlin: Dru has it.").And.Contain("Lyra slips the question.");
        public_.Should().NotContainEquivalentOf("bluff");
        h.Profile(id, "Conan").Credibility.Should().Be(PlayerProfile.StartingCredibility - 1);
        h.Party(id).Social().Journal.Should().Contain(e => e.Event.Type == SocialEventTypes.AskRevealed);
    }

    [Fact]
    [CoversError(ErrorCodes.Social.NothingToReveal)]
    public async Task Reveal_NeedsAnAnswer_AndCannotBeRepeated()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?"));

        using var early = await ProblemBody.ReadAsync(await h.RevealAsync(id, ask, "Conan"), HttpStatusCode.Conflict);
        (await h.ReplyAsync(id, ask, "Merlin", "honest", "Dru")).EnsureSuccessStatusCode();
        (await h.RevealAsync(id, ask, "Conan")).EnsureSuccessStatusCode();
        using var again = await ProblemBody.ReadAsync(await h.RevealAsync(id, ask, "Conan"), HttpStatusCode.Conflict);

        early.Code.Should().Be("social.nothing_to_reveal");
        again.Code.Should().Be("social.nothing_to_reveal");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.NotAsker)]
    public async Task OnlyTheAsker_CanReveal()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?"));
        (await h.ReplyAsync(id, ask, "Merlin", "honest", "Dru")).EnsureSuccessStatusCode();

        using var problem = await ProblemBody.ReadAsync(await h.RevealAsync(id, ask, "Merlin"), HttpStatusCode.Forbidden);

        problem.Code.Should().Be("social.not_asker");
        h.Notifier.Events.Should().NotContain(e => e.Text.Contains("lays a whisper bare"));
    }

    [Fact]
    public async Task Reveal_WithAWorthlessWord_IsRefused()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?"));
        (await h.ReplyAsync(id, ask, "Merlin", "honest", "Dru")).EnsureSuccessStatusCode();
        h.Profile(id, "Conan").Credibility = 0;

        using var problem = await ProblemBody.ReadAsync(await h.RevealAsync(id, ask, "Conan"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("social.not_enough_credibility");
    }

    [Fact]
    public async Task Reveal_OfAnExpiredWhisper_IsNotFound()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?"));
        (await h.ReplyAsync(id, ask, "Merlin", "honest", "Dru")).EnsureSuccessStatusCode();
        h.Clock.Advance(TimeSpan.FromMinutes(11));

        using var problem = await ProblemBody.ReadAsync(await h.RevealAsync(id, ask, "Conan"), HttpStatusCode.NotFound);

        problem.Code.Should().Be("social.ask_not_found");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.DuplicateAsk)]
    public async Task ANearCopyOfARecentWhisper_IsRefused_ButAnotherQuestionIsFine()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?")).EnsureSuccessStatusCode();
        h.Clock.Advance(TimeSpan.FromSeconds(100));

        using var problem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Conan", ["Lyra"], "who has the KEY"), HttpStatusCode.Conflict);
        (await h.AskAsync(id, "Conan", ["Lyra"], "Did you see the dragon?")).EnsureSuccessStatusCode();

        problem.Code.Should().Be("social.duplicate_ask");
    }

    [Fact]
    public async Task ACopyIsAllowedAgain_OnceTheWindowHasPassed()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var first = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?"));
        (await h.ReplyAsync(id, first, "Merlin", "dodge")).EnsureSuccessStatusCode();
        h.Clock.Advance(TimeSpan.FromMinutes(6));

        (await h.AskAsync(id, "Conan", ["Lyra"], "Who has the key?")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public class AskTextTests
{
    [Theory]
    [InlineData("Who has the key?", "who has the KEY", true)]
    [InlineData("Who has the key?", "Did you see the dragon?", false)]
    [InlineData("", "anything", false)]
    [InlineData("take the gold", "take the gold and run away fast", false)]
    public void Similar_ComparesWordSets(string a, string b, bool expected) =>
        DungeonForge.Application.Features.Social.Asks.AskText.Similar(a, b).Should().Be(expected);
}

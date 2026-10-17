using System.Net;
using System.Text.Json;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Social;

public class AskSliceTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [CoversError(ErrorCodes.Social.AsksUnavailable)]
    public async Task ThreePlayersCannotWhisper_FourCan()
    {
        using var h = SocialHarness.Create(factory);
        var three = await h.PartyAsync(
            [new { name = "Conan", @class = "warrior" }, new { name = "Merlin", @class = "mage" }, new { name = "Lyra", @class = "bard" }]);
        var four = await h.PartyAsync();

        using var problem = await ProblemBody.ReadAsync(await h.AskAsync(three, "Conan", ["Merlin"], "Psst?"), HttpStatusCode.Conflict);
        var allowed = await h.AskAsync(four, "Conan", ["Merlin"], "Psst?");

        problem.Code.Should().Be("social.asks_unavailable");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var meThree = await h.MeAsync(three, "Conan");
        using var meFour = await h.MeAsync(four, "Conan");
        meThree.RootElement.GetProperty("asksAvailable").GetBoolean().Should().BeFalse();
        meFour.RootElement.GetProperty("asksAvailable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task APrivateAsk_ReachesOnlyItsRecipients_CostsATokenAndAppearsInTheirInbox()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();

        var response = await h.AskAsync(id, "Conan", ["Merlin"], "Do you trust Dru?");

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("tokensLeft").GetInt32().Should().Be(2);
        h.PrivateTexts("Merlin", PrivateKinds.Ask).Should().ContainSingle().Which.Should().Contain("Do you trust Dru?");
        SocialHarness.Four.Where(n => n != "Merlin").Should().OnlyContain(n => !h.PrivateTexts(n, PrivateKinds.Ask).Any());
        h.Notifier.PublicTraffic.Should().NotContain("Do you trust Dru?");

        using var merlin = await h.MeAsync(id, "Merlin");
        merlin.RootElement.GetProperty("inbox").GetArrayLength().Should().Be(1);
        using var lyra = await h.MeAsync(id, "Lyra");
        lyra.RootElement.GetProperty("inbox").GetArrayLength().Should().Be(0);
        lyra.RootElement.GetProperty("sent").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("Conan")]
    [InlineData("Nobody")]
    [CoversError(ErrorCodes.Social.InvalidRecipients)]
    public async Task Whispering_ToYourselfOrAStranger_IsRejected(string target)
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();

        using var problem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Conan", [target], "Hello"), HttpStatusCode.BadRequest);

        problem.Code.Should().Be("social.invalid_recipients");
        problem.Root.GetProperty("errors").GetProperty("to").GetArrayLength().Should().Be(1);
    }

    [Fact]
    [CoversError(ErrorCodes.Social.AskCooldown)]
    public async Task AskingAgainTooSoon_IsRateLimited_UntilTheCooldownPasses()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await h.AskAsync(id, "Conan", ["Merlin"], "First")).EnsureSuccessStatusCode();

        using var problem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Conan", ["Lyra"], "Second"), HttpStatusCode.TooManyRequests);
        h.Clock.Advance(TimeSpan.FromSeconds(46));
        var later = await h.AskAsync(id, "Conan", ["Lyra"], "Second");

        problem.Code.Should().Be("social.ask_cooldown");
        later.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheSamePairHasALongerCooldownThanTheSender()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await h.AskAsync(id, "Conan", ["Merlin"], "First")).EnsureSuccessStatusCode();
        h.Clock.Advance(TimeSpan.FromSeconds(46));

        using var problem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Again"), HttpStatusCode.TooManyRequests);
        h.Clock.Advance(TimeSpan.FromSeconds(46));

        problem.Code.Should().Be("social.ask_cooldown");
        (await h.AskAsync(id, "Conan", ["Merlin"], "Again")).StatusCode.Should().Be(HttpStatusCode.OK, "90s have passed for this pair");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.TooManyOpenAsks)]
    public async Task ThirdOpenWhisper_IsRefused()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await h.AskAsync(id, "Conan", ["Merlin"], "One")).EnsureSuccessStatusCode();
        h.Clock.Advance(TimeSpan.FromSeconds(50));
        (await h.AskAsync(id, "Conan", ["Lyra"], "Two")).EnsureSuccessStatusCode();
        h.Clock.Advance(TimeSpan.FromSeconds(50));

        using var problem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Conan", ["Dru"], "Three"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("social.too_many_open_asks");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.NotEnoughTokens)]
    public async Task WithoutShards_AWhisperIsRefused()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        h.Profile(id, "Conan").AskTokens = 0;

        using var problem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Hello"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("social.not_enough_tokens");
    }

    [Fact]
    public async Task AccusingWithAWorthlessWord_CostsDouble()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        h.Profile(id, "Conan").Credibility = 0;

        var response = await h.AskAsync(id, "Conan", ["Merlin"], "You took the gold", "accusation");

        response.EnsureSuccessStatusCode();
        h.Profile(id, "Conan").AskTokens.Should().Be(1);
    }

    [Fact]
    public async Task AReply_ReachesTheAsker_AndABluffReadsLikeAnHonestAnswer()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var honest = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Who has the key?"));
        h.Clock.Advance(TimeSpan.FromSeconds(100));
        var bluffed = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Lyra"], "Who has the key?"));

        (await h.ReplyAsync(id, honest, "Merlin", "honest", "Dru has it")).EnsureSuccessStatusCode();
        (await h.ReplyAsync(id, bluffed, "Lyra", "bluff", "Dru has it")).EnsureSuccessStatusCode();

        var seen = h.PrivateTexts("Conan", PrivateKinds.Ask).ToArray();
        seen.Should().Contain("Merlin: Dru has it").And.Contain("Lyra: Dru has it");
        seen.Should().NotContain(t => t.Contains("bluff", StringComparison.OrdinalIgnoreCase));
        using var me = await h.MeAsync(id, "Conan");
        me.RootElement.GetProperty("sent")[0].GetProperty("replies")[0].GetProperty("summary").GetString().Should().Be("Dru has it");
    }

    [Fact]
    public async Task ADodge_ShowsAsADodge()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Where were you?"));

        (await h.ReplyAsync(id, ask, "Merlin", "dodge")).EnsureSuccessStatusCode();

        h.PrivateTexts("Conan", PrivateKinds.Ask).Should().Contain("Merlin slips the question.");
    }

    [Fact]
    [CoversError(ErrorCodes.Social.AskNotFound)]
    [CoversError(ErrorCodes.Social.NotRecipient)]
    [CoversError(ErrorCodes.Social.AlreadyReplied)]
    public async Task ReplyRules_UnknownThread_WrongPlayer_AndRepeats()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "Hello"));

        using var unknown = await ProblemBody.ReadAsync(await h.ReplyAsync(id, "ask_999", "Merlin", "honest"), HttpStatusCode.NotFound);
        using var stranger = await ProblemBody.ReadAsync(await h.ReplyAsync(id, ask, "Lyra", "honest"), HttpStatusCode.Forbidden);
        (await h.ReplyAsync(id, ask, "Merlin", "honest", "Hi")).EnsureSuccessStatusCode();
        using var again = await ProblemBody.ReadAsync(await h.ReplyAsync(id, ask, "Merlin", "dodge"), HttpStatusCode.Conflict);

        unknown.Code.Should().Be("social.ask_not_found");
        stranger.Code.Should().Be("social.not_recipient");
        again.Code.Should().Be("social.already_replied");
    }

    [Fact]
    public async Task AWhisper_ExpiresOnItsOwn_AndItsBodyIsPurged()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin"], "A secret"));
        h.Clock.Advance(TimeSpan.FromMinutes(11));

        using var merlin = await h.MeAsync(id, "Merlin");
        using var problem = await ProblemBody.ReadAsync(await h.ReplyAsync(id, ask, "Merlin", "honest"), HttpStatusCode.NotFound);

        merlin.RootElement.GetProperty("inbox").GetArrayLength().Should().Be(0);
        problem.Code.Should().Be("social.ask_not_found");
        h.Party(id).Social().Asks.Single().Text.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenEveryRecipientAnswersTheSameWay_TheAskerIsToldTheyStandTogether()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin", "Lyra"], "Do we trust Dru?"));

        (await h.ReplyAsync(id, ask, "Merlin", "honest", "No")).EnsureSuccessStatusCode();
        h.PrivateTexts("Conan", PrivateKinds.Narrator).Should().NotContain("They stand together.");
        (await h.ReplyAsync(id, ask, "Lyra", "honest", "No")).EnsureSuccessStatusCode();

        h.PrivateTexts("Conan", PrivateKinds.Narrator).Should().Contain("They stand together.");
    }

    [Fact]
    public async Task WhenRecipientsDisagree_NoConsensusNoticeIsSent()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin", "Lyra"], "Do we trust Dru?"));

        (await h.ReplyAsync(id, ask, "Merlin", "honest", "No")).EnsureSuccessStatusCode();
        (await h.ReplyAsync(id, ask, "Lyra", "dodge")).EnsureSuccessStatusCode();

        h.PrivateTexts("Conan", PrivateKinds.Narrator).Should().NotContain("They stand together.");
    }

    [Fact]
    public async Task ASupervisorlessGame_StillRunsWhispers_ButNothingIsJudged()
    {
        using var h = SocialHarness.Create(factory, new() { ["Social:Supervision"] = "false" });
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Conan", ["Merlin", "Lyra"], "Do we trust Dru?"));

        (await h.ReplyAsync(id, ask, "Merlin", "honest", "No")).EnsureSuccessStatusCode();
        (await h.ReplyAsync(id, ask, "Lyra", "honest", "No")).EnsureSuccessStatusCode();

        h.PrivateTexts("Conan", PrivateKinds.Narrator).Should().BeEmpty();
        h.Party(id).Social().Journal.Should().OnlyContain(e => e.Decision.Rule == "R-OFF");
    }
}

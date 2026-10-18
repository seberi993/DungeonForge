using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Social.Asks;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Features.Social.Supervision;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.Extensions.Options;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Social;

public class SupervisionTests
{
    private static readonly ManualTime Clock = new(DateTimeOffset.UtcNow);

    private static SocialPipeline Pipeline(params ISocialRule[] rules) =>
        new(new RuleSupervisor(rules.Length == 0 ? [new UnanimousPeerRule(), new TheftClaimRule(), new SubgroupConsensusRule()] : rules),
            new SocialModules(Options.Create(new SocialOptions())), Clock);

    // A chaos party in which Dru has pocketed the vault gold (the dungeon knows; nobody has proven it).
    private static Party PartyWhereDruStole()
    {
        var party = PartyTestBuilders.NewParty(PartyTestBuilders.ChaosOn);
        party.DropVault(20, null);
        party.ClaimVault("Dru", VaultChoice.Pocket, new ScriptedRoller(99));
        party.PocketedGold("Dru").Should().BePositive();
        return party;
    }

    private static Decision Accuse(Party party, SocialPipeline pipeline, string accused, AskChoice choice, string text = "Did you take the vault gold?")
    {
        party.Social().Profile("Conan");
        var e = pipeline.NewEvent(party, SocialEventTypes.AskReply, 1, [accused],
            new Dictionary<string, string> { ["from"] = "Conan", ["kind"] = "Accusation", ["choice"] = choice.ToString(), ["text"] = text });
        return pipeline.Process(party, e);
    }

    [Fact]
    public void GuiltyAndHonest_IsRewardedAndTheAccuserVindicated()
    {
        var party = PartyWhereDruStole();

        var d = Accuse(party, Pipeline(), "Dru", AskChoice.Honest);

        d.Verdict.Should().Be(Verdict.Celebrate);
        d.Rule.Should().Be("R-THEFT");
        party.Social().Profile("Dru").Trust.Should().Be(1);
        party.Social().Profile("Conan").Credibility.Should().Be(PlayerProfile.StartingCredibility + 1);
    }

    [Fact]
    public void GuiltyAndBluffing_IsPunishedHard_AndTheLiarLosesCredibility()
    {
        var party = PartyWhereDruStole();

        var d = Accuse(party, Pipeline(), "Dru", AskChoice.Bluff);

        d.Verdict.Should().Be(Verdict.Punish);
        d.Severity.Should().Be(2);
        var dru = party.Social().Profile("Dru");
        dru.Trust.Should().Be(-1);
        dru.Menace.Should().Be(1);
        dru.Credibility.Should().Be(PlayerProfile.StartingCredibility - 1);
        party.Social().Profile("Conan").Credibility.Should().Be(PlayerProfile.StartingCredibility + 1);
    }

    [Fact]
    public void GuiltyAndDodging_IsOnlyMildlyPunished()
    {
        var party = PartyWhereDruStole();

        var d = Accuse(party, Pipeline(), "Dru", AskChoice.Dodge);

        d.Verdict.Should().Be(Verdict.Punish);
        d.Severity.Should().Be(1);
        party.Social().Profile("Dru").Credibility.Should().Be(PlayerProfile.StartingCredibility);
    }

    [Fact]
    public void InnocentAndHonest_PunishesTheFalseAccuser()
    {
        var party = PartyTestBuilders.NewParty(PartyTestBuilders.ChaosOn);

        var d = Accuse(party, Pipeline(), "Dru", AskChoice.Honest);

        d.Verdict.Should().Be(Verdict.Punish);
        var conan = party.Social().Profile("Conan");
        conan.Credibility.Should().Be(PlayerProfile.StartingCredibility - 1);
        conan.Trust.Should().Be(-1);
        party.Social().Profile("Dru").Trust.Should().Be(0);
    }

    [Fact]
    public void AmbiguousEvidence_StaysUnresolved_WithOnlyAHint()
    {
        var party = PartyTestBuilders.NewParty(PartyTestBuilders.ChaosOn);

        var d = Accuse(party, Pipeline(), "Dru", AskChoice.Dodge);

        d.Verdict.Should().Be(Verdict.Unresolved);
        d.Consequences.Should().ContainSingle().Which.Should().BeOfType<Consequence.Notice>();
        party.Social().Profile("Dru").Trust.Should().Be(0);
        party.Social().Profile("Conan").Credibility.Should().Be(PlayerProfile.StartingCredibility);
    }

    [Fact]
    public void AnAccusationThatIsNotAboutTheft_IsNotJudgedAsTheft()
    {
        var party = PartyWhereDruStole();

        var d = Accuse(party, Pipeline(), "Dru", AskChoice.Bluff, "You are a terrible singer");

        d.Verdict.Should().Be(Verdict.Ignore);
        party.Social().Profile("Dru").Trust.Should().Be(0);
    }

    [Fact]
    public void EveryDecision_IsAuditedWithItsRuleRationaleAndEvidence()
    {
        var party = PartyWhereDruStole();
        var pipeline = Pipeline();

        Accuse(party, pipeline, "Dru", AskChoice.Bluff);

        var audit = party.Social().Journal.Should().ContainSingle().Subject;
        audit.Decision.Rule.Should().Be("R-THEFT");
        audit.Decision.Rationale.Should().NotBeNullOrWhiteSpace();
        audit.Event.Type.Should().Be(SocialEventTypes.AskReply);
        audit.Seed.Should().NotBe(0);
    }

    [Fact]
    public void RulesAreIndependent_RemovingOneLeavesTheOthersWorking()
    {
        var party = PartyWhereDruStole();
        var withoutTheft = Pipeline(new UnanimousPeerRule(), new SubgroupConsensusRule());

        var d = Accuse(party, withoutTheft, "Dru", AskChoice.Bluff);

        d.Verdict.Should().Be(Verdict.Ignore, "no rule claims the event");
        d.Rule.Should().Be("R-NONE");
        var consensus = withoutTheft.NewEvent(party, SocialEventTypes.AskConsensus, 1, ["Merlin", "Lyra"], new Dictionary<string, string> { ["from"] = "Conan" });
        withoutTheft.Process(party, consensus).Verdict.Should().Be(Verdict.Celebrate);
    }

    [Fact]
    public void Rules_AreTriedInOrder_FirstClaimWins()
    {
        var party = PartyTestBuilders.NewParty();
        var first = new StubRule("A", 1, Verdict.Punish);
        var second = new StubRule("B", 2, Verdict.Celebrate);

        var supervisor = new RuleSupervisor([second, first]);
        var e = new SocialEvent("ne_1", "x", 1, [], new Dictionary<string, string>(), []);

        supervisor.Judge(new SocialContext(party, Clock.GetUtcNow()), e).Rule.Should().Be("A");
    }

    [Fact]
    public void TheApplier_ClampsWhateverARuleOrAModelAsksFor()
    {
        var social = new PartySocial(1);
        social.Profile("Conan");
        var wild = new Decision("e", Verdict.Punish, 3,
        [
            new Consequence.AxisShift("Conan", Axis.Valor, 500),
            new Consequence.Reputation("Conan", 99, -99),
            new Consequence.Credibility("Conan", -50),
            new Consequence.Tokens("Conan", 50),
            new Consequence.Notice(new SocialNotice(null, "narrator", new string('x', 1000))),
            new Consequence.AxisShift("Ghost", Axis.Valor, 5),
        ], "R-TEST", "test");

        ConsequenceApplier.Apply(social, wild);

        var conan = social.Profile("Conan");
        conan.Axes[Axis.Valor].Should().Be(ConsequenceApplier.MaxAxisShift);
        conan.Trust.Should().Be(ConsequenceApplier.MaxReputationShift);
        conan.Menace.Should().Be(-ConsequenceApplier.MaxReputationShift);
        conan.Credibility.Should().Be(PlayerProfile.StartingCredibility - 1);
        conan.AskTokens.Should().Be(PlayerProfile.StartingTokens + ConsequenceApplier.MaxTokenShift);
        social.Outbox.Single().Text.Length.Should().Be(ConsequenceApplier.MaxNoticeLength);
        social.FindProfile("Ghost").Should().BeNull("a consequence never invents a player");
    }

    [Fact]
    public void TheJournal_KeepsOnlyTheMostRecentEntries()
    {
        var social = new PartySocial(1);
        for (var i = 0; i < PartySocial.JournalCapacity + 25; i++)
        {
            var e = new SocialEvent($"ne_{i}", "x", 1, [], new Dictionary<string, string>(), []);
            social.Record(new AuditEntry(e, new Decision(e.Id, Verdict.Ignore, 0, [], "R", "r"), 1, Clock.GetUtcNow()));
        }

        social.Journal.Should().HaveCount(PartySocial.JournalCapacity);
        social.Journal.First().Event.Id.Should().Be("ne_25");
    }

    private sealed class StubRule(string id, int order, Verdict verdict) : ISocialRule
    {
        public string Id { get; } = id;

        public int Order { get; } = order;

        public Decision? Evaluate(SocialContext context, SocialEvent socialEvent) =>
            new(socialEvent.Id, verdict, 1, [], Id, "stub");
    }
}

public class GameStateConsequenceTests
{
    private static readonly ManualTime Clock = new(DateTimeOffset.UtcNow);

    private static Decision Wild(params Consequence[] consequences) =>
        new("e", Verdict.Punish, 3, consequences, "R-TEST", "test");

    [Fact]
    public void Hp_IsClampedAndAppliedToTheNamedMember_AndFlagsAStateChange()
    {
        var party = PartyTestBuilders.NewParty();
        var before = party.Members.Single(m => m.Name == "Conan").Hp;

        ConsequenceApplier.Apply(party, Wild(new Consequence.Hp("Conan", -99)));

        party.Members.Single(m => m.Name == "Conan").Hp.Should().Be(before - ConsequenceApplier.MaxHpShift);
        party.Social().StateChanged.Should().BeTrue();
    }

    [Fact]
    public void Healing_IsClampedToTheMaxAndNeverRevivesTheDefeated()
    {
        var party = PartyTestBuilders.NewParty();
        party.ApplyMemberOutcomes([new DamageResult("Conan", 10), new DamageResult("Merlin", 99)], 0);
        var conan = party.Members.Single(m => m.Name == "Conan");
        var hurt = conan.Hp;

        ConsequenceApplier.Apply(party, Wild(new Consequence.Hp("Conan", 99), new Consequence.Hp("Merlin", 5)));

        conan.Hp.Should().Be(Math.Min(conan.MaxHp, hurt + ConsequenceApplier.MaxHpShift));
        party.Members.Single(m => m.Name == "Merlin").IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void Gold_IsClamped_NeverGoesNegative_AndANamelessGhostIsIgnored()
    {
        var party = PartyTestBuilders.NewParty();
        party.AdvanceTurn(4);

        ConsequenceApplier.Apply(party, Wild(new Consequence.Gold(-99), new Consequence.Hp("Ghost", -5)));

        party.Gold.Should().Be(0);
        ConsequenceApplier.Apply(party, Wild(new Consequence.Gold(99)));
        party.Gold.Should().Be(ConsequenceApplier.MaxGoldShift);
    }

    [Fact]
    public void ABluffingThief_LosesHp_AndTheStateChangeIsFlaggedForBroadcast()
    {
        var party = PartyTestBuilders.NewParty(PartyTestBuilders.ChaosOn);
        party.DropVault(20, null);
        party.ClaimVault("Dru", VaultChoice.Pocket, new ScriptedRoller(99));
        var dru = party.Members.Single(m => m.Name == "Dru");
        var before = dru.Hp;
        var pipeline = new SocialPipeline(
            new RuleSupervisor([new TheftClaimRule()]), new SocialModules(Options.Create(new SocialOptions())), Clock);

        pipeline.Process(party, pipeline.NewEvent(party, SocialEventTypes.AskReply, 1, ["Dru"], new Dictionary<string, string>
        {
            ["from"] = "Conan", ["kind"] = "Accusation", ["choice"] = "Bluff", ["text"] = "Did you take the vault gold?",
        }));

        dru.Hp.Should().BeLessThan(before);
        party.Social().StateChanged.Should().BeTrue();
    }
}

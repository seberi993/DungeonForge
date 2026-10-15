using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class GaslightingTests
{
    [Fact]
    public void EveryLivingMember_GetsOneClaim_AboutSomeoneElse()
    {
        var party = ChaosTestSupport.NewParty(new PartyModifiers(Chaos: true));

        var lies = Gaslighting.Generate(party, new ScriptedRoller(0, 1, 2, 3));

        lies.Select(l => l.Target).Should().BeEquivalentTo(party.Members.Where(m => !m.IsDefeated).Select(m => m.Name));
        foreach (var lie in lies)
        {
            lie.Text.Should().NotBeNullOrWhiteSpace();
            lie.Text.Should().NotContain("{");
        }
    }

    [Fact]
    public async Task Dispatcher_SendsNothing_OutsideChaos()
    {
        var notifier = new ChaosRecordingNotifier();
        var dispatcher = new ChaosDispatcher(notifier, new ScriptedRoller(0), Microsoft.Extensions.Logging.Abstractions.NullLogger<ChaosDispatcher>.Instance);

        await dispatcher.DispatchGaslightingAsync(ChaosTestSupport.NewParty(new PartyModifiers()));

        notifier.Privates.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatcher_SendsWhispersPrivately_InChaos()
    {
        var notifier = new ChaosRecordingNotifier();
        var dispatcher = new ChaosDispatcher(notifier, new ScriptedRoller(0), Microsoft.Extensions.Logging.Abstractions.NullLogger<ChaosDispatcher>.Instance);
        var party = ChaosTestSupport.NewParty(new PartyModifiers(Chaos: true));

        await dispatcher.DispatchGaslightingAsync(party);

        notifier.Privates.Should().NotBeEmpty().And.OnlyContain(p => p.Message.Kind == PrivateKinds.Whisper);
        notifier.PublicTraffic.Should().BeEmpty();
    }
}

public class NarrationBrevityTests
{
    [Fact]
    public void CleanSentences_CutsAtTheLastFullSentence()
    {
        var text = "One short sentence here. Another sentence that runs well past the limit of the budget";

        NarrationText.CleanSentences(text, 40).Should().Be("One short sentence here.");
    }

    [Fact]
    public void RunFlavor_ThemeIsStablePerParty_ButVariesAcrossParties()
    {
        RunFlavor.Theme("The Brave").Should().Be(RunFlavor.Theme("The Brave"));
        Enumerable.Range(0, 20).Select(i => RunFlavor.Theme($"party {i}")).Distinct().Count().Should().BeGreaterThan(3);
    }

    [Fact]
    public void FallbackScenarios_AreVariedAcrossTurns()
    {
        foreach (var room in Enum.GetValues<RoomType>())
        {
            Enumerable.Range(0, 5).Select(t => FallbackFlavor.Scenario(room, t)).Distinct().Should().HaveCount(5);
        }
    }
}

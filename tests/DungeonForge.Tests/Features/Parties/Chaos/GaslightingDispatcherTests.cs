using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class GaslightingDispatcherTests
{
    [Fact]
    public async Task Dispatcher_SendsNothing_OutsideChaos()
    {
        var notifier = new ChaosRecordingNotifier();
        var dispatcher = new ChaosDispatcher(notifier, new ScriptedRoller(0), NullLogger<ChaosDispatcher>.Instance);

        await dispatcher.DispatchGaslightingAsync(ChaosTestSupport.NewParty(new PartyModifiers()));

        notifier.Privates.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatcher_SendsWhispersPrivately_InChaos()
    {
        var notifier = new ChaosRecordingNotifier();
        var dispatcher = new ChaosDispatcher(notifier, new ScriptedRoller(0), NullLogger<ChaosDispatcher>.Instance);
        var party = ChaosTestSupport.NewParty(new PartyModifiers(Chaos: true));

        await dispatcher.DispatchGaslightingAsync(party);

        notifier.Privates.Should().NotBeEmpty().And.OnlyContain(p => p.Message.Kind == PrivateKinds.Whisper);
        notifier.PublicTraffic.Should().BeEmpty();
    }
}

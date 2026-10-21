using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class GaslightingDispatcherTests
{
    [Fact]
    public async Task Dispatcher_SendsNothing_OutsideChaos()
    {
        var notifier = new ChaosRecordingNotifier();
        var dispatcher = new ChaosDispatcher(notifier, new ScriptedRoller(0), NullLogger<ChaosDispatcher>.Instance);

        await dispatcher.DispatchGaslightingAsync(PartyTestBuilders.NewParty(new PartyModifiers()));

        notifier.Privates.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatcher_SendsWhispersPrivately_InChaos()
    {
        var notifier = new ChaosRecordingNotifier();
        var dispatcher = new ChaosDispatcher(notifier, new ScriptedRoller(0), NullLogger<ChaosDispatcher>.Instance);
        var party = PartyTestBuilders.NewParty(new PartyModifiers(Chaos: true));

        await dispatcher.DispatchGaslightingAsync(party);

        notifier.Privates.Should().NotBeEmpty().And.OnlyContain(p => p.Message.Kind == PrivateKinds.Whisper);
        notifier.PublicTraffic.Should().BeEmpty();
    }
}

using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;

namespace DungeonForge.Api.Features.Parties.CastConfrontationVote;

public sealed class CastConfrontationVoteHandler(
    IPartyStore parties, IRoller roller, IPartyNotifier notifier, ChaosDispatcher chaos, ILogger<CastConfrontationVoteHandler> logger)
{
    public const string Operation = "Command:CastConfrontationVote";

    public async Task<Result<CastVoteResponse>> HandleAsync(Guid id, string playerName, ConfrontationVote vote)
    {
        if (!parties.TryGet(id, out var party))
        {
            return PartyErrors.NotFound(Operation, id);
        }

        var name = playerName.Trim();
        var result = party.CastVote(name, vote, roller);
        switch (result.Status)
        {
            case VoteStatus.NoConfrontation:
                return CastConfrontationVoteErrors.NoConfrontation(Operation, id);
            case VoteStatus.NotEligible:
                return CastConfrontationVoteErrors.NotEligible(Operation, id, name);
        }

        parties.Save(party);
        if (result.Resolution is not { } resolution)
        {
            await chaos.PublishAsync(id, new ChaosEvent(
                ChaosEvent.Confrontation, $"A ballot was cast ({result.Cast} of {result.Required})."));
            return new CastVoteResponse(id, false, result.Cast, result.Required);
        }

        await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.ConfrontationResolved, Verdict(resolution)));
        await BroadcastStateAsync(party);
        await chaos.AnnounceTraitorOutcomeAsync(party, party.EvaluateTraitor());
        return new CastVoteResponse(id, true, result.Cast, result.Required, JsonNamingPolicy.CamelCase.ConvertName(resolution.Outcome.ToString()));
    }

    private static string Verdict(ConfrontationResolution r)
    {
        var tally = string.Join(", ", r.Tally.Where(t => t.Value > 0).Select(t => $"{t.Key} x{t.Value}"));
        var effect = r.Outcome switch
        {
            ConfrontationVote.Forgive => "forgiven and keeps the loot",
            ConfrontationVote.DemandCut => $"made to hand {r.GoldToParty} gold to the party",
            ConfrontationVote.Banish => "banished, the loot forfeit",
            _ => $"attacked for {r.Damage} damage, the loot forfeit",
        };
        return $"Verdict on {r.Accused}: {effect}. Ballots: {tally}.";
    }

    private Task BroadcastStateAsync(Party party) => BestEffort.RunAsync(
        logger, Operation, party.Id, "PartyUpdated",
        () => notifier.PartyUpdatedAsync(PartyMapping.ToResponse(party.Snapshot()), CancellationToken.None));
}

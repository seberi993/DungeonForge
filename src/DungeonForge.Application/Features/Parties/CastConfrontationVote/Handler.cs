using System.Text.Json;
using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.Realtime;

namespace DungeonForge.Application.Features.Parties.CastConfrontationVote;

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
                ChaosEvent.Confrontation, ChaosText.BallotCast(result.Cast, result.Required)));
            return new CastVoteResponse(id, false, result.Cast, result.Required);
        }

        await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.ConfrontationResolved, ChaosText.Verdict(resolution)));
        await BroadcastStateAsync(party);
        await chaos.AnnounceTraitorOutcomeAsync(party, party.EvaluateTraitor());
        return new CastVoteResponse(id, true, result.Cast, result.Required, JsonNamingPolicy.CamelCase.ConvertName(resolution.Outcome.ToString()));
    }

    private Task BroadcastStateAsync(Party party) => BestEffort.RunAsync(
        logger, Operation, party.Id, "PartyUpdated",
        () => notifier.PartyUpdatedAsync(PartyMapping.ToResponse(party.Snapshot()), CancellationToken.None));
}

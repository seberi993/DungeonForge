using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;

namespace DungeonForge.Api.Features.Parties.ExtractRun;

// A player walks out of the dungeon. Standard runs have no extraction; with a Traitor Pact this is what can complete the
// traitor solo-extraction win condition, which is tracked apart from ordinary victory.
public sealed class ExtractRunHandler(IPartyStore parties, ChaosDispatcher chaos)
{
    public const string Operation = "Command:ExtractRun";

    public async Task<Result<ExtractResponse>> HandleAsync(Guid id, string playerName)
    {
        if (!parties.TryGet(id, out var party))
        {
            return PartyErrors.NotFound(Operation, id);
        }

        if (!party.Modifiers.TraitorPact)
        {
            return ExtractRunErrors.NoPact(Operation, id);
        }

        var name = playerName.Trim();
        if (!party.TryExtract(name))
        {
            return PartyErrors.PlayerUnknown(Operation, id, name);
        }

        parties.Save(party);
        var outcome = party.EvaluateTraitor();
        await chaos.AnnounceTraitorOutcomeAsync(party, outcome);
        return new ExtractResponse(id, JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString()));
    }
}

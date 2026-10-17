using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ExtractRun;

// A player walks out of the dungeon. Standard runs have no extraction; with a Traitor Pact this is what can complete the
// traitor solo-extraction win condition, which is tracked apart from ordinary victory.
public sealed class ExtractRunHandler(IMemoryCache cache, ChaosDispatcher chaos)
{
    public const string Operation = "Command:ExtractRun";

    public async Task<Result<ExtractResponse>> HandleAsync(Guid id, string playerName)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
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

        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
        var outcome = party.EvaluateTraitor();
        await chaos.AnnounceTraitorOutcomeAsync(party, outcome);
        return new ExtractResponse(id, JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString()));
    }
}

using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ExtractRun;

// A player walks out of the dungeon. Standard runs have no extraction; with a Traitor Pact this is what can complete the
// traitor solo-extraction win condition, which is tracked apart from ordinary victory.
public sealed class ExtractRunHandler(IMemoryCache cache, ChaosDispatcher chaos)
{
    public async Task<ExtractResult> HandleAsync(Guid id, string playerName)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return new ExtractResult(ExtractStatus.NotFound);
        }

        if (!party.Modifiers.TraitorPact)
        {
            return new ExtractResult(ExtractStatus.NoPact);
        }

        if (!party.TryExtract(playerName.Trim()))
        {
            return new ExtractResult(ExtractStatus.UnknownPlayer);
        }

        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
        var outcome = party.EvaluateTraitor();
        await chaos.AnnounceTraitorOutcomeAsync(party, outcome);
        return new ExtractResult(
            ExtractStatus.Extracted,
            new ExtractResponse(id, JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString())));
    }
}

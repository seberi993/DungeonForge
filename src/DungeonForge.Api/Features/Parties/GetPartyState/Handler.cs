using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateHandler(IMemoryCache cache)
{
    public const string Operation = "Query:GetPartyState";

    public Result<GetPartyStateResponse> Handle(Guid id) =>
        cache.TryGetValue(PartyCache.Key(id), out Party? party) && party is not null
            ? PartyMapping.ToResponse(party.Snapshot())
            : PartyErrors.NotFound(Operation, id);
}

using DungeonForge.Api.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateHandler(IMemoryCache cache)
{
    public GetPartyStateResponse? Handle(Guid id) =>
        cache.TryGetValue(PartyCache.Key(id), out Party? party) && party is not null
            ? PartyMapping.ToResponse(party.Snapshot())
            : null;
}

using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateHandler(IMemoryCache cache)
{
    public GetPartyStateResponse? Handle(Guid id) =>
        cache.TryGetValue($"party:{id}", out Party? party) && party is not null
            ? new GetPartyStateResponse(party.Id, party.Name, party.Members, party.CreatedAt, party.Hp, party.Gold, party.Turn)
            : null;
}

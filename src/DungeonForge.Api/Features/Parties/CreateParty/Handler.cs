using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.CreateParty;

public sealed class CreatePartyHandler(IMemoryCache cache, TimeProvider time)
{
    public PartyResponse Handle(CreatePartyRequest request)
    {
        var party = new Party(Guid.NewGuid(), request.Name.Trim(), [], time.GetUtcNow());

        cache.Set(PartyCache.Key(party.Id), party, PartyCache.EntryOptions());

        return new PartyResponse(party.Id, party.Name, party.Members, party.CreatedAt);
    }
}

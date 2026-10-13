using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.CreateParty;

internal sealed record Party(Guid Id, string Name, IReadOnlyList<string> Members, DateTimeOffset CreatedAt);

public sealed class CreatePartyHandler(IMemoryCache cache, TimeProvider time)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    public PartyResponse Handle(CreatePartyRequest request)
    {
        var party = new Party(Guid.NewGuid(), request.Name.Trim(), [], time.GetUtcNow());

        cache.Set($"party:{party.Id}", party, new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = Ttl,
        });

        return new PartyResponse(party.Id, party.Name, party.Members, party.CreatedAt);
    }
}

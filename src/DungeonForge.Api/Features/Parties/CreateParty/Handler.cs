using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.CreateParty;

public sealed class CreatePartyHandler(IMemoryCache cache, TimeProvider time)
{
    // Single-instance host: this lock makes code-uniqueness check + reservation atomic.
    private static readonly Lock InviteGate = new();

    public PartyResponse Handle(CreatePartyRequest request)
    {
        var id = Guid.NewGuid();
        string code;
        lock (InviteGate)
        {
            do
            {
                code = InviteCodes.Generate();
            }
            while (cache.TryGetValue(PartyCache.InviteKey(code), out _));

            cache.Set(PartyCache.InviteKey(code), id, PartyCache.EntryOptions());
        }

        var party = new Party(id, request.Name.Trim(), [], time.GetUtcNow(), InviteCode: code);
        cache.Set(PartyCache.Key(party.Id), party, PartyCache.EntryOptions());

        return new PartyResponse(party.Id, party.Name, party.Members, party.CreatedAt, InviteCodes.Format(code));
    }
}

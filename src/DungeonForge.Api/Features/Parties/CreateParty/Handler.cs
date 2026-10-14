using DungeonForge.Api.Domain;
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

            // Request was validated by the endpoint filter.
            var members = request.Members!.Select(m => new PartyMember(m.Name!.Trim(), m.Class!.Value)).ToList();
            var party = Party.Create(id, request.Name!.Trim(), members, time.GetUtcNow(), code, request.Modifiers, time);

            // Party first, then the invite key: a resolvable code never points at a missing party.
            cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
            cache.Set(PartyCache.InviteKey(code), id, PartyCache.EntryOptions());

            var snapshot = party.Snapshot();
            return new PartyResponse(snapshot.Id, snapshot.Name, PartyMapping.ToMemberDtos(snapshot), snapshot.CreatedAt, InviteCodes.Format(code));
        }
    }
}

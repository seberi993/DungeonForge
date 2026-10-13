using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateHandler(IMemoryCache cache)
{
    public GetPartyStateResponse? Handle(Guid id) =>
        cache.TryGetValue(PartyCache.Key(id), out Party? party) && party is not null
            ? ToResponse(party)
            : null;

    internal static GetPartyStateResponse ToResponse(Party party) =>
        new(party.Id, party.Name, party.Members, party.CreatedAt, party.Hp, party.Gold, party.Turn,
            party.InviteCode.Length == 0 ? "" : InviteCodes.Format(party.InviteCode));
}

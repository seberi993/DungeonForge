using DungeonForge.Api.Features.Parties.GetPartyState;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.JoinByCode;

public sealed class JoinByCodeHandler(IMemoryCache cache)
{
    public GetPartyStateResponse? Handle(string? code) =>
        InviteCodes.Normalize(code) is { } normalized
        && cache.TryGetValue(PartyCache.InviteKey(normalized), out Guid id)
        && cache.TryGetValue(PartyCache.Key(id), out Party? party)
        && party is not null
            ? GetPartyStateHandler.ToResponse(party)
            : null;
}

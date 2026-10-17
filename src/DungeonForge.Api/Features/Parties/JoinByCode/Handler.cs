using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.JoinByCode;

public sealed class JoinByCodeHandler(IMemoryCache cache)
{
    public const string Operation = "Query:JoinByCode";

    public Result<GetPartyStateResponse> Handle(string? code) =>
        InviteCodes.Normalize(code) is { } normalized
        && cache.TryGetValue(PartyCache.InviteKey(normalized), out Guid id)
        && cache.TryGetValue(PartyCache.Key(id), out Party? party)
        && party is not null
            ? PartyMapping.ToResponse(party.Snapshot())
            : PartyErrors.InviteNotFound(Operation, code);
}

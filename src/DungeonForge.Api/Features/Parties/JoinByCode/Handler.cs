using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.JoinByCode;

public sealed class JoinByCodeHandler(IPartyStore parties)
{
    public const string Operation = "Query:JoinByCode";

    public Result<GetPartyStateResponse> Handle(string? code) =>
        InviteCodes.Normalize(code) is { } normalized && parties.TryGetByInvite(normalized, out var party)
            ? PartyMapping.ToResponse(party.Snapshot())
            : PartyErrors.InviteNotFound(Operation, code);
}

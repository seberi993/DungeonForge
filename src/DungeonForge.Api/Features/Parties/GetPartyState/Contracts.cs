namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed record GetPartyStateResponse(
    Guid PartyId,
    string Name,
    IReadOnlyList<MemberDto> Members,
    DateTimeOffset CreatedAt,
    int Gold,
    int Turn,
    bool IsPartyDefeated,
    string InviteCode = "");

using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Parties;

public sealed record MemberDto(string Name, CharacterClass Class, int Hp, int MaxHp, bool IsDefeated);

public sealed record LockStatusDto(string Name, bool LockedIn);

public sealed record GetPartyStateResponse(
    Guid PartyId,
    string Name,
    IReadOnlyList<MemberDto> Members,
    DateTimeOffset CreatedAt,
    int Gold,
    int Turn,
    bool IsPartyDefeated,
    string InviteCode = "",
    IReadOnlyList<LockStatusDto>? LockStatus = null,
    int Floor = 1,
    int RoomsCleared = 0,
    int BossHp = 0,
    int BossMaxHp = 0,
    int SetupAnswered = 0,
    bool SetupPending = false);

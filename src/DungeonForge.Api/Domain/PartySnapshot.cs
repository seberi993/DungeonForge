namespace DungeonForge.Api.Domain;

public sealed record MemberLockStatus(string Name, bool LockedIn);

public sealed record MemberSnapshot(string Name, CharacterClass Class, int Hp, int MaxHp, bool IsDefeated);

public sealed record PartySnapshot(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    string InviteCode,
    int Gold,
    int Turn,
    bool IsPartyDefeated,
    IReadOnlyList<MemberSnapshot> Members,
    IReadOnlyList<MemberLockStatus> LockStatus);

namespace DungeonForge.Domain;

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
    IReadOnlyList<MemberLockStatus> LockStatus,
    int Floor = 1,
    int RoomsCleared = 0,
    int BossHp = 0,
    int BossMaxHp = 0,
    int SetupAnswered = 0,
    bool SetupPending = false);

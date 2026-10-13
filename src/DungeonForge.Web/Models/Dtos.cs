namespace DungeonForge.Web.Models;

public enum RoomType
{
    Combat,
    Chest
}

public enum CharacterClass
{
    Warrior,
    Mage,
    Druid,
    Paladin,
    Priest,
    Necromancer,
    Bard
}

public sealed record MemberRequest(string Name, CharacterClass Class);

public sealed record CreatePartyRequest(string Name, IReadOnlyList<MemberRequest> Members);

public sealed record MemberDto(string Name, CharacterClass Class, int Hp, int MaxHp, bool IsDefeated);

public sealed record PartyResponse(Guid PartyId, string Name, IReadOnlyList<MemberDto> Members, DateTimeOffset CreatedAt, string InviteCode = "");

public sealed record ExploreResponse(
    Guid PartyId,
    int Turn,
    RoomType RoomType,
    string? TargetName,
    int Damage,
    int GoldFound,
    string FlavorEvent,
    int Gold,
    bool IsPartyDefeated);

public sealed record LogEntry(int Turn, RoomType RoomType, string? TargetName, int Damage, int GoldFound, string FlavorEvent);

public sealed record PartyStateDto(
    Guid PartyId,
    string Name,
    IReadOnlyList<MemberDto> Members,
    DateTimeOffset CreatedAt,
    int Gold,
    int Turn,
    bool IsPartyDefeated,
    string InviteCode = "");

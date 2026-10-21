namespace DungeonForge.Web.Shared;

public enum RoomType
{
    Combat,
    Chest,
    Puzzle,
    Boss
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

public sealed record MemberDto(string Name, CharacterClass Class, int Hp, int MaxHp, bool IsDefeated);

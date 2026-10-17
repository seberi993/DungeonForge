using DungeonForge.Web.Models;
using DungeonForge.Web.Services;

namespace DungeonForge.Web.Tests;

/// <summary>Shared party and turn builders for the GameSession behaviour tests.</summary>
internal static class SessionData
{
    public static readonly Guid Id = Guid.NewGuid();
    public static MemberDto Member(string name, CharacterClass cls, int hp, int maxHp) => new(name, cls, hp, maxHp, hp == 0);
    public static PartyResponse Party() => new(Id, "Heroes", [Member("A", CharacterClass.Warrior, 30, 30), Member("B", CharacterClass.Mage, 16, 16)], DateTimeOffset.UtcNow);
    public static ExploreResponse Turn(int turn, RoomType room, string? target, int damage, int gold, int totalGold, bool defeated = false, string flavor = "x") =>
        new(Id, turn, room, target, damage, gold, flavor, totalGold, defeated);

    public static ExploreResponse Pending(int turn) =>
        new(Id, turn, RoomType.Combat, "A", 4, 0, "Clash", 0, false, NarrationPending: true);

    public static ExploreResponse Prompt(int turn, string scenario = "A troll\nblocks the bridge.") =>
        new(Id, turn, RoomType.Combat, null, 0, 0, scenario, 0, false, Scenario: scenario);
}

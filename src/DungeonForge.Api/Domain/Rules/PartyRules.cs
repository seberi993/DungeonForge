using DungeonForge.Shared;
namespace DungeonForge.Api.Domain.Rules;

// Party-level limits and floor progression.
public static class PartyRules
{
    public const int MaxMembers = Limits.MaxMembers;
    public const int MaxDiscussionEntries = 8;

    // A floor is RoomsPerFloor ordinary rooms followed by a boss that takes several rounds to fell.
    public const int RoomsPerFloor = 4;
    public const int BossBaseHp = 30;
    public const int BossHpPerFloor = 10;

    // Clearing a floor heals each living member by this share of their max hp.
    public const double FloorHealRatio = 0.4;

    public static int BossHpFor(int floor) => BossBaseHp + BossHpPerFloor * (floor - 1);

    public static int FloorHealFor(int maxHp) => (int)Math.Ceiling(maxHp * FloorHealRatio);
}

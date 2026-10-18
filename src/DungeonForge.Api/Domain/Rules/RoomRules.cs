namespace DungeonForge.Api.Domain.Rules;

// Game math for rooms lives here, never in the narrator or a handler.
public static class RoomRules
{
    // Wounded means total hp at or below this share of total max hp.
    public const double WoundedRatio = 0.5;

    // The rooms a roll can produce, in the order the roller's result indexes them. The boss is never rolled: it is entered
    // only when a floor's ordinary rooms run out or the boss is already awake.
    public static readonly IReadOnlyList<RoomType> Rollable = [RoomType.Combat, RoomType.Chest, RoomType.Puzzle];

    // One roller call, Next(Rollable.Count): the call order the scripted-roller tests depend on. When the players' words lean
    // toward one kind of room, ONE more call Next(100) is appended and may swap the rolled room for the favored one.
    public static RoomType Roll(IRoller roller, StoryLean lean = default)
    {
        var room = Rollable[roller.Next(Rollable.Count)];
        return lean.Favored is { } favored && roller.Next(100) < lean.SwapPercent ? favored : room;
    }

    // Each member whose own action failed takes this hit; it grows as the dungeon deepens.
    // The boss hits harder per floor and does not scale with the turn count, so a long floor stays fair.
    public static int FailDamage(RoomType room, int turn, int floor) => room switch
    {
        RoomType.Combat => 6 + turn % 3 + turn / 3,
        RoomType.Puzzle => 7 + turn % 3 + turn / 3,
        RoomType.Boss => 8 + 2 * (floor - 1) + turn % 3,
        _ => 4 + turn / 3,
    };

    // Boss gold is only paid when the boss falls.
    public static int Gold(RoomType room, int turn, int floor) => room switch
    {
        RoomType.Chest => 5 + turn,
        RoomType.Puzzle => 10 + 2 * turn,
        RoomType.Boss => 25 * floor,
        _ => 0,
    };

    public static bool IsWounded(int hp, int maxHp) => hp <= maxHp * WoundedRatio;
}

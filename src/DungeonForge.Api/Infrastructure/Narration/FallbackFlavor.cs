using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public static class FallbackFlavor
{
    private static readonly EncounterChoice[] Combat =
    [
        new("A goblin ambush springs from the shadows.", ["Hold the line", "Charge them head-on", "Hurl yourself at the chieftain"]),
        new("A skeleton guard rattles to life.", ["Shield up and wait", "Trade blows", "Smash its skull in one swing"]),
        new("A giant rat swarm pours from the walls.", ["Retreat to the doorway", "Fight them in the open", "Torch the whole nest"]),
    ];

    private static readonly EncounterChoice[] Chest =
    [
        new("A dusty chest creaks in the torchlight.", ["Pick the lock carefully", "Pry it open", "Smash the lid"]),
        new("Behind a loose stone, a forgotten purse awaits.", ["Probe for traps first", "Reach in quickly", "Yank the stone free"]),
        new("A gilded coffer glimmers beyond a gap.", ["Hook it with a rope", "Climb across", "Leap for it"]),
    ];

    private static readonly EncounterChoice[] Puzzle =
    [
        new("Glowing runes ring a sealed stone door.", ["Study the runes slowly", "Press the runes in sequence", "Smash the central glyph"]),
        new("A riddling statue blocks the passage.", ["Ponder its riddle", "Answer boldly", "Wrench the statue aside"]),
        new("Floor tiles shift beneath a locked gate.", ["Test each tile carefully", "Dash across the tiles", "Leap for the lever"]),
    ];

    private static readonly string[] CombatSuccess =
    [
        "Your nerve holds and the foe falls back, battered and broken.",
        "A decisive blow ends the fight before it can turn.",
    ];

    private static readonly string[] CombatFailure =
    [
        "The plan unravels and the enemy lands a painful strike.",
        "You misjudge the moment and pay for it in blood.",
    ];

    private static readonly string[] ChestSuccess =
    [
        "The lid gives way and coins spill into your hands.",
        "Your gamble pays off; the treasure is yours.",
    ];

    private static readonly string[] ChestFailure =
    [
        "The mechanism jams and the treasure slips away.",
        "Nothing but dust and splinters rewards the effort.",
    ];

    private static readonly string[] PuzzleSuccess =
    [
        "The mechanism clicks, the way opens, and a hidden cache glitters inside.",
        "Together you crack the riddle and the vault yields its treasure.",
    ];

    private static readonly string[] PuzzleFailure =
    [
        "The trap springs and a jolt of force sweeps through the party.",
        "A wrong move triggers the lock's defenses, and stone and sparks fly.",
    ];

    public static EncounterChoice Choices(RoomType room, int turn)
    {
        var set = room switch { RoomType.Combat => Combat, RoomType.Puzzle => Puzzle, _ => Chest };
        return set[Index(turn, set.Length)];
    }

    public static string Outcome(RoomType room, bool success, int turn)
    {
        var set = (room, success) switch
        {
            (RoomType.Combat, true) => CombatSuccess,
            (RoomType.Combat, false) => CombatFailure,
            (RoomType.Puzzle, true) => PuzzleSuccess,
            (RoomType.Puzzle, false) => PuzzleFailure,
            (_, true) => ChestSuccess,
            _ => ChestFailure,
        };
        return set[Index(turn, set.Length)];
    }

    private static int Index(int turn, int length) => (int)(Math.Abs((long)turn) % length);
}

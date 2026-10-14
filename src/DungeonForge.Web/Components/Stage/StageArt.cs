using DungeonForge.Web.Models;

namespace DungeonForge.Web.Components.Stage;

public enum Archetype
{
    Warrior,
    Mage,
    Cleric,
    Rogue
}

// Hand-drawn sprites. Hero sprites come in a near (5 rows) and a far (3 rows) size; the far size is also used when a
// big party has to share one row. Enemy and prop art comes from AsciiArt (the narrator's scene tags).
public static class StageArt
{
    public static Archetype ArchetypeOf(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Mage or CharacterClass.Necromancer => Archetype.Mage,
        CharacterClass.Priest or CharacterClass.Druid => Archetype.Cleric,
        CharacterClass.Bard => Archetype.Rogue,
        _ => Archetype.Warrior
    };

    public static IReadOnlyList<string> Hero(Archetype archetype, bool small, bool act) => (archetype, small, act) switch
    {
        (Archetype.Warrior, false, false) => WarriorIdle,
        (Archetype.Warrior, false, true) => WarriorAct,
        (Archetype.Mage, false, false) => MageIdle,
        (Archetype.Mage, false, true) => MageAct,
        (Archetype.Rogue, false, false) => RogueIdle,
        (Archetype.Rogue, false, true) => RogueAct,
        (Archetype.Cleric, false, false) => ClericIdle,
        (Archetype.Cleric, false, true) => ClericAct,
        (Archetype.Warrior, true, false) => [@"(o) ^", @"/#\ |", @"/ \ +"],
        (Archetype.Warrior, true, true) => [@"(>)", @"/#\=>", @"/ \"],
        (Archetype.Mage, true, false) => [@"(o) *", @"/~\ |", @"/_\ |"],
        (Archetype.Mage, true, true) => [@"(O)\*/", @"/~\-|", @"/_\ |"],
        (Archetype.Rogue, true, false) => [@"(o)", @"<|>", @"/ \"],
        (Archetype.Rogue, true, true) => [@"(>)", @" \|--->", @"/ \"],
        (Archetype.Cleric, true, false) => [@"(o) +", @"/+\", @"/ \"],
        _ => [@"\(^)/", @" /+\", @" / \"]
    };

    // A hero at zero HP lies where they fell.
    public static readonly IReadOnlyList<string> Fallen = [@"", @"(x_x)", @"'---'"];

    // What is left of a creature once it is beaten.
    public static readonly IReadOnlyList<string> Rubble = [@" _.xX._ ", @"'--------'"];

    private static readonly string[] WarriorIdle = [@"  _^_   ^", @" (o.o)  |", @" /[#]\--+", @"  | |   |", @" _/ \_"];
    private static readonly string[] WarriorAct = [@"  _^_", @" (>.<)", @" /[#]\--+===>", @"  | |   |", @" _/ \_"];
    private static readonly string[] MageIdle = [@"   /\    *", @"  /__\   |", @"  (o.o)  |", @" /|~~~|\ |", @"  /___\  |"];
    private static readonly string[] MageAct = [@"   /\   \*/", @"  /__\   |", @"  (O.O)--|", @" /|~~~|\ |", @"  /___\  |"];
    private static readonly string[] RogueIdle = [@"  .---.", @" ( o.o )", @" <\===/>", @"  /| |\", @" _/   \_"];
    private static readonly string[] RogueAct = [@"  .---.", @" ( >.< )", @"  \===/---->", @"   /|\", @"  _/ \_"];
    private static readonly string[] ClericIdle = [@"   _+_", @"  (o.o)", @"  /(+)\", @"  /| |\", @"  _/ \_"];
    private static readonly string[] ClericAct = [@"  \_+_/", @"  (^.^)", @" \/(+)\/", @"   | |", @"  _/ \_"];

    // Rows of one 2-frame scene, left-aligned (the common indent removed) so the art can be centred by its own width.
    public static IReadOnlyList<string> SceneRows(string? scene, RoomType? room, int frame)
    {
        var frames = AsciiArt.Frames(scene, room);
        var lines = frames[frame % frames.Length].Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(l => l.TrimEnd())
            .ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        var indent = lines.Where(l => l.Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return [.. lines.Select(l => l.Length >= indent ? l[indent..] : l)];
    }

    // The same creature with crossed-out eyes.
    public static IReadOnlyList<string> Struck(IReadOnlyList<string> rows) =>
        [.. rows.Select(r => r.Replace("o,o", "x,x").Replace("o o", "x x").Replace("O   O", "X   X").Replace("( o", "( x").Replace("(o", "(x"))];

    public static int Width(IReadOnlyList<string> rows) => rows.Count == 0 ? 0 : rows.Max(r => r.TrimEnd().Length);
}

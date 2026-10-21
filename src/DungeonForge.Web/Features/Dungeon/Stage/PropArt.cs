namespace DungeonForge.Web.Features.Dungeon.Stage;

public enum PropSlot { WallLeft, WallRight, Ceiling, Floor, Air }

public enum PropMotion { Cycle, Grow, Wander, Dangle, Flutter, Rattle, Float, Drift, Fall }

// One thing the narrator can put in the hall: its words, its frames and how it moves.
public sealed record PropKind(string Id, string[] Stems, string Hue, PropSlot[] Slots, PropMotion Motion, int FrameMs, string[][] Frames)
{
    public int Width => Frames.Max(f => f.Max(r => r.Length));
    public int Height => Frames.Max(f => f.Length);
    public string Class => "st-p-" + Hue;
}

// A prop with a place in the hall. Index orders the pop-in; Phase keeps two props of one kind out of step.
public sealed record PlacedProp(string Word, PropKind Kind, int X, int Y, int Index, int Phase);

// The lexicon that turns a highlighted word into something that moves in the corridor. Unknown words still get a
// sparkle, so every highlight is visible.
public static class PropArt
{
    public const double BurstMs = 350;

    private static string[] F(params string[] rows) => rows;

    private static readonly PropSlot[] Walls = [PropSlot.WallLeft, PropSlot.WallRight];

    public static readonly PropKind Sparkle = new("sparkle", [], "arcane", [PropSlot.Air], PropMotion.Cycle, 260,
        [F(" + ", "+*+", " + "), F(" . ", ".*.", " . "), F(" * ", "*+*", " * ")]);

    public static readonly IReadOnlyList<PropKind> Kinds =
    [
        new("torch", ["torch", "candle", "lantern", "flame", "fire", "brazier", "ember", "lamp", "blaze"], "fire", Walls, PropMotion.Cycle, 120,
            [F(" ) ", "(*)", " # "), F(" ( ", ")*(", " # "), F(" ^ ", "(^)", " # ")]),
        new("rat", ["rat", "mouse", "vermin", "rodent", "roach", "beetle", "insect", "scuttl"], "bone", [PropSlot.Floor], PropMotion.Wander, 140,
            [F("~(o>"), F("-(o>")]),
        new("spider", ["spider", "web", "cobweb", "thread"], "bone", [PropSlot.Ceiling], PropMotion.Dangle, 400,
            [F(" | ", " | ", "/o\\"), F(" | ", " | ", "\\o/")]),
        new("bat", ["bat", "wing", "flock", "swarm", "moth"], "ghost", [PropSlot.Air], PropMotion.Flutter, 160,
            [F("/\\o/\\"), F("\\/o\\/")]),
        new("skeleton", ["skeleton", "bone", "skull", "remains", "corpse", "undead", "zombie", "ribs"], "bone", [PropSlot.WallLeft, PropSlot.Floor, PropSlot.WallRight], PropMotion.Rattle, 300,
            [F(" .-. ", "(o o)", " |=| "), F(" .-. ", "(o o)", " |#| ")]),
        new("ghost", ["ghost", "spirit", "wraith", "wight", "specter", "spectre", "phantom", "apparition", "haunt", "soul", "banshee"], "ghost", [PropSlot.Air], PropMotion.Float, 500,
            [F(" .-. ", "(o o)", "|   |", "'v^v'"), F(" .-. ", "(o o)", "|   |", "'^v^'")]),
        new("drip", ["water", "drip", "dripping", "puddle", "acid", "slime", "ooze", "leak", "damp", "moisture", "sewer", "stream"], "cold", [PropSlot.Ceiling], PropMotion.Fall, 400,
            [F("=*="), F("=o=")]),
        new("smoke", ["smoke", "mist", "fog", "steam", "haze", "vapor", "vapour", "gas", "cloud", "dust", "sand", "shadow", "gloom", "darkness", "stench"], "mist", [PropSlot.Air], PropMotion.Drift, 450,
            [F(" ~~~  ", "~~~~~ "), F("  ~~~ ", " ~~~~~"), F(" ~~~~ ", "~~~~  ")]),
        new("chain", ["chain", "shackle", "manacle", "rope", "noose", "hook", "cage", "gibbet"], "iron", [PropSlot.Ceiling, .. Walls], PropMotion.Cycle, 500,
            [F("o ", " o", "o ", " o"), F(" o", "o ", " o", "o ")]),
        new("statue", ["statue", "idol", "gargoyle", "carving", "effigy", "eyes", "gaze", "pillar", "column", "mask", "relief"], "gold", Walls, PropMotion.Cycle, 800,
            [F(" .-. ", "|o o|", "'---'"), F(" .-. ", "|* *|", "'---'")]),
        new("rune", ["rune", "glyph", "sigil", "symbol", "inscription", "altar", "circle", "ritual", "crystal", "orb", "magic", "portal", "shrine"], "arcane", [PropSlot.Floor, PropSlot.WallLeft, PropSlot.WallRight], PropMotion.Cycle, 350,
            [F(" /\\ ", "<+>", " \\/ "), F(" /\\ ", "<*>", " \\/ "), F(" /\\ ", "<#>", " \\/ ")]),
        new("door", ["door", "gate", "portcullis", "hatch", "trapdoor", "archway", "exit", "passage", "tunnel", "stair"], "wood", Walls, PropMotion.Cycle, 900,
            [F("┌──┐", "│  │", "│ o│", "└──┘"), F("┌──┐", "│▒ │", "│▒o│", "└──┘")]),
        new("lever", ["lever", "switch", "button", "handle", "crank", "mechanism", "gear", "pedestal", "plate", "trap", "tripwire", "wire", "spike", "dart"], "iron", [PropSlot.Floor, PropSlot.WallRight, PropSlot.WallLeft], PropMotion.Cycle, 600,
            [F("o ", " \\", "[_]"), F(" o", "/ ", "[_]")]),
        new("gold", ["gold", "coin", "treasure", "coffer", "gem", "jewel", "hoard", "loot", "riches", "key", "chest", "crown", "goblet"], "gold", [PropSlot.Floor], PropMotion.Cycle, 280,
            [F(".*.", "$$$"), F("*..", "$$$"), F("..*", "$$$"), F(".+.", "$$$")]),
        new("blood", ["blood", "stain", "gore", "pool", "carcass", "entrails", "wound", "slick"], "red", [PropSlot.Floor], PropMotion.Grow, 1200,
            [F("~"), F(" ~~~ ", "~~~~~"), F("~~~~~~~", " ~~~~~ ", "  ~~~  ")]),
        new("vine", ["vine", "moss", "root", "tendril", "fungus", "fungi", "mushroom", "mold", "mould", "weed", "bramble", "creeper", "thorn", "lichen"], "green", [PropSlot.Ceiling, .. Walls], PropMotion.Cycle, 450,
            [F("( ", " )", "( ", " )"), F(" (", ") ", " (", ") ")]),
        new("banner", ["banner", "flag", "tapestry", "curtain", "cloth", "cape", "robe", "pennant", "rag"], "red", Walls, PropMotion.Cycle, 600,
            [F("|=|", "|*|", "\\ /"), F("|=|", "|*|", "/ \\")]),
        new("watcher", ["goblin", "orc", "troll", "kobold", "imp", "demon", "creature", "monster", "beast", "dragon", "guard", "figure", "stranger", "cultist", "bandit", "thief"], "red", Walls, PropMotion.Cycle, 650,
            [F("o   o"), F("o   o"), F("o   o"), F("-   -")])
    ];

    // The first word of the phrase that the lexicon knows; anything else is a sparkle.
    public static PropKind Resolve(string? phrase)
    {
        foreach (var raw in (phrase ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = new string([.. raw.Where(char.IsLetter)]).ToLowerInvariant();
            if (w.Length == 0) continue;
            foreach (var kind in Kinds)
            {
                if (kind.Stems.Any(s => Matches(w, s))) return kind;
            }
        }

        return Sparkle;
    }

    // Short stems must match the whole word (plus a plural), or "bat" would claim "battle" and "rat" would claim "rather".
    private static bool Matches(string w, string stem) =>
        stem.Length <= 4
            ? w == stem || w == stem + "s" || w == stem + "es"
            : w.StartsWith(stem, StringComparison.Ordinal);

    public static void Draw(CharGrid g, PlacedProp p, double clock, double ageMs)
    {
        if (ageMs < 0) return;
        var k = p.Kind;
        if (ageMs < BurstMs)
        {
            g.Set(p.X + k.Width / 2.0, p.Y + k.Height / 2.0, ageMs < 120 ? '*' : ageMs < 240 ? '+' : '.', "st-fxy");
            return;
        }

        var t = ageMs - BurstMs + p.Phase;
        var s = t / 1000.0;
        var frame = k.Motion == PropMotion.Grow
            ? Math.Min(k.Frames.Length - 1, (int)((ageMs - BurstMs) / k.FrameMs))
            : (int)(t / k.FrameMs) % k.Frames.Length;
        double dx = 0, dy = 0;
        switch (k.Motion)
        {
            case PropMotion.Wander: dx = 5 * Tri(s / 3); break;
            case PropMotion.Dangle: dy = 1.5 * Math.Sin(s * 2); break;
            case PropMotion.Flutter: dx = 3 * Math.Sin(s * 2.2); dy = 2 * Math.Sin(s * 3.1); break;
            case PropMotion.Rattle: if (t % 2200 < 500) dx = (int)(t / 60) % 2 == 0 ? 1 : -1; break;
            case PropMotion.Float: dx = 2 * Math.Sin(s * 0.7); dy = 1.5 * Math.Sin(s * 1.3); break;
            case PropMotion.Drift: dx = 4 * Math.Sin(s * 0.6); dy = -0.5 * Math.Sin(s * 1.1); break;
        }

        g.Sprite(p.X + dx, p.Y + dy, k.Frames[frame], k.Class);

        if (k.Motion == PropMotion.Fall)
        {
            var q = t % 1600 / 1600;
            g.Set(p.X + k.Width / 2.0, p.Y + k.Height + q * 5, q < 0.85 ? 'o' : '_', k.Class);
        }
    }

    // -1..1 and back, once per unit.
    private static double Tri(double x) => 1 - 4 * Math.Abs(x - Math.Floor(x) - 0.5);
}

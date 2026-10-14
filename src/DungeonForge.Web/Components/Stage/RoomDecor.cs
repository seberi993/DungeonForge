namespace DungeonForge.Web.Components.Stage;

public sealed record FrameStyle(char H, char V, char TopLeft, char TopRight, char BottomLeft, char BottomRight);

// How one encounter dresses the hall: stone, floor and light picked from the narration's seed and mood words, plus a
// prop for every highlighted word.
public sealed record RoomDecor(
    FrameStyle Frame, char Floor, char Ceiling, char Door,
    int[] TorchDepths, bool TorchLeft, bool TorchRight,
    IReadOnlyList<PlacedProp> Props)
{
    public const int MaxProps = 6;

    private static readonly FrameStyle[] Frames =
    [
        new('─', '│', '┌', '┐', '└', '┘'),
        new('═', '║', '╔', '╗', '╚', '╝'),
        new('-', '|', '+', '+', '+', '+')
    ];

    private static readonly char[] Floors = ['░', '▒', ':', '.'];
    private static readonly char[] Ceilings = ['.', ',', '\'', '`'];
    private static readonly char[] Doors = ['▓', '#', '▒'];
    private static readonly int[][] Torches = [[2, 4], [2], [4], [3]];

    // Slot positions (top-left); the right wall is the left wall mirrored. Clear of the foe (x 20-46) and the heroes (y 21+).
    private static readonly (int X, int Y)[] WallPool = [(4, 9), (8, 14), (12, 5), (3, 15)];
    private static readonly (int X, int Y)[] CeilingPool = [(24, 1), (38, 1), (14, 2), (48, 2)];
    private static readonly (int X, int Y)[] FloorPool = [(22, 20), (38, 20), (28, 20), (14, 20)];
    private static readonly (int X, int Y)[] AirPool = [(20, 5), (40, 5), (30, 3), (44, 9)];

    public static RoomDecor Default { get; } = new(Frames[0], '░', '.', '▓', [2, 4], true, true, []);

    // FNV-1a: stable across runs and platforms, unlike string.GetHashCode.
    public static uint Seed(string? text, int turn)
    {
        var h = 2166136261u;
        foreach (var c in text ?? "") h = (h ^ c) * 16777619u;
        return (h ^ (uint)turn) * 16777619u;
    }

    public static RoomDecor Compose(string? text, int turn)
    {
        var seed = Seed(text, turn);
        var lower = (text ?? "").ToLowerInvariant();
        var floor = Floors[seed >> 3 & 3];
        var torches = Torches[seed >> 7 & 3];
        if (lower.Contains("flood") || lower.Contains("water") || lower.Contains("puddle")) floor = '~';
        if (lower.Contains("dark") || lower.Contains("shadow") || lower.Contains("gloom")) torches = [4];
        else if (lower.Contains("torch") || lower.Contains("blaze") || lower.Contains("fire")) torches = [2, 4];

        var sides = seed >> 11 & 3;
        var used = new HashSet<(PropSlot, int)>();
        var props = new List<PlacedProp>();
        foreach (var (word, key) in TypewriterText.Parse(text))
        {
            if (!key || props.Count >= MaxProps) continue;
            var kind = PropArt.Resolve(word);
            var index = props.Count;
            if (TryPlace(kind, seed, index, used, out var slot, out var pos))
            {
                var x = slot == PropSlot.WallRight ? Corridor.Width - pos.X - kind.Width : pos.X;
                // Floor props sit on the strip between the foe's feet and the heroes (bottom row 19).
                var y = slot == PropSlot.Floor ? 20 - kind.Height : pos.Y;
                props.Add(new PlacedProp(word, kind, x, y, index, (int)(seed >> index & 0x3FF)));
            }
        }

        return new RoomDecor(Frames[seed % 3], floor, Ceilings[seed >> 5 & 3], Doors[(seed >> 9) % 3],
            torches, sides != 1, sides != 2, props);
    }

    private static bool TryPlace(PropKind kind, uint seed, int index, HashSet<(PropSlot, int)> used, out PropSlot slot, out (int X, int Y) pos)
    {
        var order = kind.Slots.Length;
        var start = (int)((seed >> 13) + (uint)index) % order;
        for (var pass = 0; pass < 2; pass++)
        {
            // The second pass lets a prop take any free spot rather than vanish.
            var slots = pass == 0 ? Enumerable.Range(0, order).Select(i => kind.Slots[(start + i) % order]) : Enum.GetValues<PropSlot>();
            foreach (var s in slots)
            {
                var pool = Pool(s);
                for (var i = 0; i < pool.Length; i++)
                {
                    var n = (i + index) % pool.Length;
                    if (!used.Add((s, n))) continue;
                    slot = s;
                    pos = pool[n];
                    return true;
                }
            }
        }

        slot = PropSlot.Air;
        pos = default;
        return false;
    }

    private static (int X, int Y)[] Pool(PropSlot s) => s switch
    {
        PropSlot.WallLeft or PropSlot.WallRight => WallPool,
        PropSlot.Ceiling => CeilingPool,
        PropSlot.Floor => FloorPool,
        _ => AirPool
    };
}

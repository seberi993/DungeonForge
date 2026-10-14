namespace DungeonForge.Web.Components;

public readonly record struct MinimapPosition(int Row, int Col);

// Pure, deterministic snake path: row 0 runs left to right, row 1 right to left, and so on,
// so consecutive rooms are always adjacent tiles.
public static class MinimapLayout
{
    public const int Columns = 5;

    public static MinimapPosition Position(int index, int columns = Columns)
    {
        var row = index / columns;
        var offset = index % columns;
        return new MinimapPosition(row, row % 2 == 0 ? offset : columns - 1 - offset);
    }

    public static IReadOnlyList<MinimapPosition> Layout(int count, int columns = Columns) =>
        [.. Enumerable.Range(0, count).Select(i => Position(i, columns))];

    public static int RowCount(int count, int columns = Columns) => (count + columns - 1) / columns;
}

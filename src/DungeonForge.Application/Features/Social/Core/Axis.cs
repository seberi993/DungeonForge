namespace DungeonForge.Application.Features.Social.Core;

// The hidden personality axes. They never leave the server; clients only ever see tags and flavor.
public enum Axis
{
    Valor,
    Cunning,
    Empathy,
    Discipline,
    Chaos,
    Greed,
    Lore,
    Resolve,
}

public sealed class AxisVector
{
    public const int Limit = 100;

    private readonly int[] _values = new int[Enum.GetValues<Axis>().Length];

    public int this[Axis axis] => _values[(int)axis];

    // Adds delta and clamps to +-limit (default +-Limit). Returns the change actually applied.
    public int Add(Axis axis, int delta, int limit = Limit)
    {
        var before = _values[(int)axis];
        _values[(int)axis] = Math.Clamp(before + delta, -limit, limit);
        return _values[(int)axis] - before;
    }

    // Axes ordered by absolute strength, strongest first; ties break by enum order so results are deterministic.
    public IReadOnlyList<Axis> Strongest(int count) =>
        [.. Enum.GetValues<Axis>().OrderByDescending(a => Math.Abs(this[a])).ThenBy(a => (int)a).Take(count)];

    public IReadOnlyDictionary<Axis, int> ToDictionary() => Enum.GetValues<Axis>().ToDictionary(a => a, a => this[a]);
}

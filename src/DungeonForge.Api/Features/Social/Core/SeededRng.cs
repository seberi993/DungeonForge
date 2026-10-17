namespace DungeonForge.Api.Features.Social.Core;

// SplitMix64: tiny, stable across runtimes and platforms, so a logged seed replays the exact same outcome.
public sealed class SeededRng(ulong seed)
{
    private ulong _state = seed;

    public static ulong Derive(ulong seed, string purpose, int counter = 0)
    {
        var h = seed ^ 0x9E3779B97F4A7C15UL;
        foreach (var c in purpose)
        {
            h = Mix(h + c);
        }

        return Mix(h + (ulong)counter);
    }

    public static SeededRng For(ulong seed, string purpose, int counter = 0) => new(Derive(seed, purpose, counter));

    public ulong NextUInt64() => Mix(_state += 0x9E3779B97F4A7C15UL);

    // Integer in [0, max).
    public int Next(int max) => max <= 1 ? 0 : (int)(NextUInt64() % (ulong)max);

    // Integer in [min, max] inclusive.
    public int Between(int min, int max) => min + Next(max - min + 1);

    // Double in [0, 1).
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

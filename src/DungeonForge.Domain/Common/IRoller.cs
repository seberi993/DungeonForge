namespace DungeonForge.Domain;

public interface IRoller
{
    /// <summary>Returns an integer in [0, max).</summary>
    int Next(int max);
}

public sealed class RandomRoller : IRoller
{
    public int Next(int max) => Random.Shared.Next(max);
}

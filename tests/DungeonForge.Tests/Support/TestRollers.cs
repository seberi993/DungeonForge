using DungeonForge.Api.Domain;

namespace DungeonForge.Tests.Support;

/// <summary>Deterministic roller: Next(3) is the room roll (0 = combat, 1 = chest, 2 = puzzle); any other max is the success roll.</summary>
internal sealed class FixedRoller(int room, int success = PartyTestBuilders.Succeed) : IRoller
{
    public int Next(int max) => max == 3 ? room : success;
}

/// <summary>Room rolls come from a queue (last value repeats); any other max is the success roll.</summary>
internal sealed class SequenceRoller(int success, params int[] rooms) : IRoller
{
    private int _i;

    public int Next(int max) => max == 3 ? rooms[Math.Min(Interlocked.Increment(ref _i) - 1, rooms.Length - 1)] : success;
}

/// <summary>Replays scripted values (modulo max); the last value repeats once the script is exhausted.</summary>
internal sealed class ScriptedRoller(params int[] values) : IRoller
{
    private int _i;

    public int Next(int max) => values[Math.Min(_i++, values.Length - 1)] % max;
}

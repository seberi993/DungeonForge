using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Narration;
using Microsoft.Extensions.Options;

namespace DungeonForge.Infrastructure.Narration;

public readonly record struct KeyLease(int Index, string Key);

public interface IKeyPool
{
    int Count { get; }

    // Next non-cooling key in round-robin order, or null when every key is cooling.
    KeyLease? Next();

    void Cooldown(int index, TimeSpan duration);
}

public sealed class KeyPool : IKeyPool
{
    private readonly string[] _keys;
    private readonly long[] _cooledUntilTicks;
    private readonly TimeProvider _clock;
    private int _cursor = -1;

    public KeyPool(IOptions<NarrationOptions> options, TimeProvider clock)
    {
        _keys = options.Value.ApiKeys;
        _cooledUntilTicks = new long[_keys.Length];
        _clock = clock;
    }

    public int Count => _keys.Length;

    public KeyLease? Next()
    {
        var now = _clock.GetUtcNow().UtcTicks;
        for (var attempt = 0; attempt < _keys.Length; attempt++)
        {
            var index = (int)((uint)Interlocked.Increment(ref _cursor) % (uint)_keys.Length);
            if (Interlocked.Read(ref _cooledUntilTicks[index]) <= now)
            {
                return new KeyLease(index, _keys[index]);
            }
        }

        return null;
    }

    public void Cooldown(int index, TimeSpan duration) =>
        Interlocked.Exchange(ref _cooledUntilTicks[index], _clock.GetUtcNow().UtcTicks + duration.Ticks);
}

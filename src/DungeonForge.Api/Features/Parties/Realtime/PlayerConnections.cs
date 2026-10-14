using System.Collections.Concurrent;

namespace DungeonForge.Api.Features.Parties.Realtime;

// Which SignalR connection belongs to which player of which party, so private messages reach exactly one browser.
// Latest registration wins (a reconnect replaces the stale connection). Ephemeral, like all game state.
public sealed class PlayerConnections
{
    private readonly ConcurrentDictionary<(Guid PartyId, string Player), string> _byPlayer = new();

    private static (Guid, string) Key(Guid partyId, string player) => (partyId, player.Trim().ToUpperInvariant());

    public void Register(Guid partyId, string player, string connectionId) => _byPlayer[Key(partyId, player)] = connectionId;

    public bool TryGet(Guid partyId, string player, out string connectionId) =>
        _byPlayer.TryGetValue(Key(partyId, player), out connectionId!);

    public void Remove(string connectionId)
    {
        foreach (var entry in _byPlayer.Where(e => e.Value == connectionId))
        {
            _byPlayer.TryRemove(new KeyValuePair<(Guid, string), string>(entry.Key, connectionId));
        }
    }
}

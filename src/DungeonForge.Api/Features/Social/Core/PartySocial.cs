using DungeonForge.Api.Features.Social.Asks;
using DungeonForge.Api.Features.Social.Peers;

namespace DungeonForge.Api.Features.Social.Core;

// All hidden social state of one party. Never reaches PartySnapshot. Every access happens under Party.SyncRoot
// (use Party.Social, which hands this object out; callers lock the party), so this type has no locking of its own.
public sealed class PartySocial(ulong runSeed)
{
    public const int JournalCapacity = 200;

    private readonly Dictionary<string, PlayerProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<AuditEntry> _journal = new();
    private int _ids;

    // Replay handle: every seeded outcome (abilities, prompt picks, noise) derives from it and the purpose string.
    public ulong RunSeed { get; } = runSeed;

    // Abilities are generated once, when the last member answered the seed question (or the host forces the start).
    public bool Finalized { get; set; }

    public List<PeerRound> PeerRounds { get; } = [];

    // rater -> round currently shown to them.
    public Dictionary<string, string> PeerAssignments { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<AskThread> Asks { get; } = [];

    // Ability each member plans to use on the turn being locked in (by the client's member id). Consumed at resolve.
    public Dictionary<Guid, string> Intents { get; } = [];

    // Notices produced under the lock; the handler delivers them to clients once the lock is released.
    public List<SocialNotice> Outbox { get; } = [];

    public Dictionary<string, DateTimeOffset> SenderCooldowns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<(string From, string To), DateTimeOffset> PairCooldowns { get; } = [];

    public IReadOnlyCollection<PlayerProfile> Profiles => _profiles.Values;

    public IReadOnlyCollection<AuditEntry> Journal => _journal;

    public PlayerProfile Profile(string name)
    {
        if (!_profiles.TryGetValue(name, out var profile))
        {
            _profiles[name] = profile = new PlayerProfile(name);
        }

        return profile;
    }

    public PlayerProfile? FindProfile(string name) => _profiles.GetValueOrDefault(name);

    public string NextId(string prefix) => $"{prefix}_{++_ids}";

    public void Record(AuditEntry entry)
    {
        _journal.Enqueue(entry);
        while (_journal.Count > JournalCapacity)
        {
            _journal.Dequeue();
        }
    }

    public IReadOnlyList<SocialNotice> DrainOutbox()
    {
        var notices = Outbox.ToArray();
        Outbox.Clear();
        return notices;
    }
}

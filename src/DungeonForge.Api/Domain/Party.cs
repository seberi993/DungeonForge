namespace DungeonForge.Api.Domain;

// Mutable aggregate shared via cache. Mutators and Snapshot() lock SyncRoot internally (re-entrant, so handlers may hold it
// across a multi-step turn). Members can only be mutated through Party.
public sealed class Party
{
    private Party(Guid id, string name, PartyMember[] members, DateTimeOffset createdAt, string inviteCode)
    {
        Id = id;
        Name = name;
        Members = Array.AsReadOnly(members);
        CreatedAt = createdAt;
        InviteCode = inviteCode;
    }

    private int _gold;
    private int _turn;
    private PendingEncounter? _pending;

    public Lock SyncRoot { get; } = new();

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    public string InviteCode { get; }

    public int Gold { get { lock (SyncRoot) { return _gold; } } }

    public int Turn { get { lock (SyncRoot) { return _turn; } } }

    public IReadOnlyList<PartyMember> Members { get; }

    public bool IsPartyDefeated { get { lock (SyncRoot) { return Members.All(m => m.IsDefeated); } } }

    public static Party Create(Guid id, string name, IReadOnlyList<PartyMember> members, DateTimeOffset createdAt, string inviteCode = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (members is null || members.Count == 0)
        {
            throw new ArgumentException("A party needs at least one member.", nameof(members));
        }

        return new Party(id, name, members.ToArray(), createdAt, inviteCode);
    }

    // TODO: resistances are not modeled yet; `element` is accepted but ignored (all elements deal the same damage).
    public DamageResult TakeDamage(int amount, ElementType element = ElementType.Physical)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        lock (SyncRoot)
        {
            // First max in roster order wins ties.
            PartyMember? target = null;
            foreach (var m in Members)
            {
                if (!m.IsDefeated && (target is null || m.Threat > target.Threat))
                {
                    target = m;
                }
            }

            return target is null
                ? new DamageResult(string.Empty, 0)
                : new DamageResult(target.Name, target.TakeDamage(amount));
        }
    }

    public int Heal(string memberName, int amount)
    {
        var member = Members.FirstOrDefault(m => m.Name == memberName)
            ?? throw new ArgumentException($"No party member named '{memberName}'.", nameof(memberName));
        lock (SyncRoot)
        {
            return member.Heal(amount);
        }
    }

    public void AdvanceTurn(int goldFound = 0)
    {
        lock (SyncRoot)
        {
            _turn++;
            _gold += goldFound;
        }
    }

    public PendingEncounter? Pending { get { lock (SyncRoot) { return _pending; } } }

    // First writer wins; returns whichever encounter is pending afterwards.
    public PendingEncounter TrySetPending(PendingEncounter encounter)
    {
        lock (SyncRoot)
        {
            return _pending ??= encounter;
        }
    }

    // Applies a resolved choice atomically: damage (null when none), gold, turn advance, pending cleared.
    public DamageResult? ApplyChoiceOutcome(int damage, int gold)
    {
        lock (SyncRoot)
        {
            var hit = damage > 0 ? TakeDamage(damage) : null;
            AdvanceTurn(gold);
            _pending = null;
            return hit;
        }
    }

    public PartySnapshot Snapshot()
    {
        lock (SyncRoot)
        {
            return new PartySnapshot(
                Id, Name, CreatedAt, InviteCode, _gold, _turn, IsPartyDefeated,
                Members.Select(m => new MemberSnapshot(m.Name, m.Class, m.Hp, m.MaxHp, m.IsDefeated)).ToArray());
        }
    }
}

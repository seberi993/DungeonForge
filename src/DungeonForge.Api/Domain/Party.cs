namespace DungeonForge.Api.Domain;

// Mutable aggregate shared via cache: mutate under SyncRoot; read via Snapshot() (which takes the same lock).
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

    public Lock SyncRoot { get; } = new();

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    public string InviteCode { get; }

    public int Gold { get; private set; }

    public int Turn { get; private set; }

    public IReadOnlyList<PartyMember> Members { get; }

    public bool IsPartyDefeated => Members.All(m => m.IsDefeated);

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

    public int Heal(string memberName, int amount)
    {
        var member = Members.FirstOrDefault(m => m.Name == memberName)
            ?? throw new ArgumentException($"No party member named '{memberName}'.", nameof(memberName));
        return member.Heal(amount);
    }

    public void AdvanceTurn(int goldFound = 0)
    {
        Turn++;
        Gold += goldFound;
    }

    public PartySnapshot Snapshot()
    {
        lock (SyncRoot)
        {
            return new PartySnapshot(
                Id, Name, CreatedAt, InviteCode, Gold, Turn, IsPartyDefeated,
                Members.Select(m => new MemberSnapshot(m.Name, m.Class, m.Hp, m.MaxHp, m.IsDefeated)).ToArray());
        }
    }
}

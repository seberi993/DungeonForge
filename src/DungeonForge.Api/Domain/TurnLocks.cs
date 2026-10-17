namespace DungeonForge.Api.Domain;

// Who has locked in their action this turn, keyed by the client-generated MemberId. Part of a Party: it has no lock of its own,
// the caller holds the party's SyncRoot.
internal sealed class TurnLocks
{
    private readonly List<PlayerAction> _locks = [];

    public int Count => _locks.Count;

    public void Clear() => _locks.Clear();

    // A re-lock replaces the member's earlier action in place.
    public void Lock(PlayerAction action)
    {
        var at = _locks.FindIndex(a => a.MemberId == action.MemberId);
        if (at >= 0)
        {
            _locks[at] = action;
        }
        else
        {
            _locks.Add(action);
        }
    }

    public bool Release(Guid memberId) => _locks.RemoveAll(a => a.MemberId == memberId) > 0;

    // Per roster member. Locked actions are matched to members by name first; any left over (a client playing under another
    // name) fill the remaining unlocked slots in roster order so the displayed count always equals Count.
    public IReadOnlyList<MemberLockStatus> Status(IReadOnlyList<PartyMember> members)
    {
        var locked = new bool[members.Count];
        var leftover = 0;
        foreach (var action in _locks)
        {
            var free = Enumerable.Range(0, locked.Length).FirstOrDefault(i => !locked[i] && members[i].Name == action.Name, -1);
            if (free >= 0)
            {
                locked[free] = true;
            }
            else
            {
                leftover++;
            }
        }

        for (var i = 0; i < locked.Length && leftover > 0; i++)
        {
            if (!locked[i])
            {
                locked[i] = true;
                leftover--;
            }
        }

        return members.Select((m, i) => new MemberLockStatus(m.Name, locked[i])).ToArray();
    }
}

using DungeonForge.Api.Domain.Rules;

namespace DungeonForge.Api.Domain;

// BossNext: the next room is the boss (woken or still standing). FloorCleared: the boss fell and the party descended.
public sealed record FloorProgress(bool BossNext, bool FloorCleared);

// Mutable aggregate shared via cache. Mutators and Snapshot() lock SyncRoot internally (re-entrant, so handlers may hold it
// across a multi-step turn). Members can only be mutated through Party.
public sealed partial class Party
{
    private Party(Guid id, string name, PartyMember[] members, DateTimeOffset createdAt, string inviteCode, PartyModifiers modifiers, TimeProvider time)
    {
        Id = id;
        Name = name;
        _members = members;
        CreatedAt = createdAt;
        InviteCode = inviteCode;
        Modifiers = modifiers;
        _time = time;
    }

    // Copy-on-write: a join swaps in a new array, so unlocked readers always enumerate a stable roster.
    private PartyMember[] _members;

    private int _gold;
    private int _turn;
    private int _floor = 1;
    private int _roomsCleared;
    private int _bossHp;
    private int _bossMaxHp;
    private PendingEncounter? _pending;
    private RoomType? _awaitingRoom;
    private int _awaitingTurn;
    private readonly List<PlayerAction> _locks = [];

    public Lock SyncRoot { get; } = new();

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    public string InviteCode { get; }

    public int Gold { get { lock (SyncRoot) { return _gold; } } }

    public int Turn { get { lock (SyncRoot) { return _turn; } } }

    public int Floor { get { lock (SyncRoot) { return _floor; } } }

    public int RoomsCleared { get { lock (SyncRoot) { return _roomsCleared; } } }

    public int BossHp { get { lock (SyncRoot) { return _bossHp; } } }

    public int BossMaxHp { get { lock (SyncRoot) { return _bossMaxHp; } } }

    public IReadOnlyList<PartyMember> Members => Array.AsReadOnly(Volatile.Read(ref _members));

    // Adds a hero after creation. False when the roster is full or the name is taken (case-insensitive).
    public bool TryAddMember(PartyMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        lock (SyncRoot)
        {
            if (_members.Length >= PartyRules.MaxMembers
                || _members.Any(m => string.Equals(m.Name, member.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            Volatile.Write(ref _members, [.. _members, member]);
            return true;
        }
    }

    public bool IsPartyDefeated { get { lock (SyncRoot) { return Members.All(m => m.IsDefeated || IsOut(m.Name)); } } }

    public static Party Create(
        Guid id, string name, IReadOnlyList<PartyMember> members, DateTimeOffset createdAt, string inviteCode = "", PartyModifiers? modifiers = null,
        TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (members is null || members.Count == 0)
        {
            throw new ArgumentException("A party needs at least one member.", nameof(members));
        }

        return new Party(id, name, members.ToArray(), createdAt, inviteCode, modifiers ?? new PartyModifiers(), time ?? TimeProvider.System);
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
            _locks.Clear();
        }
    }

    public PendingEncounter? Pending { get { lock (SyncRoot) { return _pending; } } }


    // Room of the scenario being generated (rolled in C#); null when idle or a scenario is already pending.
    public RoomType? AwaitingRoom { get { lock (SyncRoot) { return _awaitingRoom; } } }

    // Claims the right to generate the next scenario. False when one is pending or already being generated, which
    // guarantees a single narrator call per turn.
    public bool TryBeginScenario(RoomType room)
    {
        lock (SyncRoot)
        {
            if (_pending is not null || _awaitingRoom is not null)
            {
                return false;
            }

            _awaitingRoom = room;
            _awaitingTurn = _turn;
            return true;
        }
    }

    // Compare-and-set completion: only the first writer for the claimed turn wins (client submit vs. server deadline).
    public bool TryCompleteScenario(int turn, PendingEncounter encounter)
    {
        lock (SyncRoot)
        {
            if (_awaitingRoom is null || _awaitingTurn != turn)
            {
                return false;
            }

            CompleteScenario(encounter);
            return true;
        }
    }

    public PendingEncounter CompleteScenario(PendingEncounter encounter)
    {
        lock (SyncRoot)
        {
            _awaitingRoom = null;
            _locks.Clear();
            return _pending = encounter;
        }
    }

    public void AbortScenario()
    {
        lock (SyncRoot)
        {
            _awaitingRoom = null;
        }
    }

    // One action per member per turn: a resubmit replaces the earlier one in place. False when no encounter is pending.
    public bool TrySubmitAction(PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (SyncRoot)
        {
            if (_pending is null)
            {
                return false;
            }

            var list = _pending.Discussion?.ToList() ?? [];
            var at = list.FindIndex(a => a.MemberId == action.MemberId);
            if (at >= 0)
            {
                list[at] = action;
            }
            else if (list.Count >= PartyRules.MaxDiscussionEntries)
            {
                return false;
            }
            else
            {
                list.Add(action);
            }

            _pending = _pending with { Discussion = list };
            return true;
        }
    }

    // ---- Synchronized turn locks, keyed by the client-generated MemberId ----

    // Locks the member in with their action (a re-lock replaces the text). False when no encounter is pending or the
    // discussion is full.
    public bool TryLockIn(PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (SyncRoot)
        {
            if (!TrySubmitAction(action))
            {
                return false;
            }

            var at = _locks.FindIndex(a => a.MemberId == action.MemberId);
            if (at >= 0)
            {
                _locks[at] = action;
            }
            else
            {
                _locks.Add(action);
            }

            return true;
        }
    }

    public bool Unlock(Guid memberId)
    {
        lock (SyncRoot)
        {
            var removed = _locks.RemoveAll(a => a.MemberId == memberId) > 0;
            if (removed && _pending?.Discussion is { } discussion)
            {
                _pending = _pending with { Discussion = discussion.Where(a => a.MemberId != memberId).ToList() };
            }

            return removed;
        }
    }


    public int LockedCount { get { lock (SyncRoot) { return _locks.Count; } } }

    // Defeated members cannot act, so they never hold the party up.
    public int RequiredLocks { get { lock (SyncRoot) { return Math.Max(1, Members.Count(m => !m.IsDefeated && !IsOut(m.Name))); } } }

    public bool AllLockedIn { get { lock (SyncRoot) { return _pending is not null && _locks.Count >= RequiredLocks; } } }

    // Per roster member. Locked actions are matched to members by name first; any left over (a client playing under another
    // name) fill the remaining unlocked slots in roster order so the displayed count always equals LockedCount.
    public IReadOnlyList<MemberLockStatus> LockStatus
    {
        get
        {
            lock (SyncRoot)
            {
                var locked = new bool[Members.Count];
                var leftover = 0;
                foreach (var action in _locks)
                {
                    var free = Enumerable.Range(0, locked.Length).FirstOrDefault(i => !locked[i] && Members[i].Name == action.Name, -1);
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

                return Members.Select((m, i) => new MemberLockStatus(m.Name, locked[i])).ToArray();
            }
        }
    }



    // Applies a resolved turn where each failed member took their own hit: damage per named member, gold, turn advance,
    // pending cleared. Unknown or already-defeated members are skipped; the applied hits are returned in order.
    public IReadOnlyList<DamageResult> ApplyMemberOutcomes(IReadOnlyList<DamageResult> hits, int gold)
    {
        lock (SyncRoot)
        {
            var applied = new List<DamageResult>();
            foreach (var hit in hits)
            {
                var member = Members.FirstOrDefault(m => m.Name == hit.TargetName);
                if (member is null || member.IsDefeated || hit.Damage <= 0) continue;
                applied.Add(new DamageResult(member.Name, member.TakeDamage(hit.Damage)));
            }

            AdvanceTurn(gold);
            SettleVaultIfWiped();
            _pending = null;
            return applied;
        }
    }

    // Records a resolved room. An ordinary room counts towards the boss; the last one wakes it. In the boss room the
    // party's passed actions wear the boss down and, once it falls, the party heals and descends to the next floor.
    public FloorProgress AdvanceProgress(RoomType room, int bossDamage)
    {
        lock (SyncRoot)
        {
            if (room != RoomType.Boss)
            {
                _roomsCleared++;
                var wakes = _roomsCleared >= PartyRules.RoomsPerFloor;
                if (wakes)
                {
                    _bossMaxHp = PartyRules.BossHpFor(_floor);
                    _bossHp = _bossMaxHp;
                }

                return new FloorProgress(wakes, false);
            }

            _bossHp = Math.Max(0, _bossHp - bossDamage);
            if (_bossHp > 0)
            {
                return new FloorProgress(true, false);
            }

            foreach (var m in Members)
            {
                m.Heal(PartyRules.FloorHealFor(m.MaxHp));
            }

            _floor++;
            _roomsCleared = 0;
            _bossMaxHp = 0;
            return new FloorProgress(false, true);
        }
    }

    public PartySnapshot Snapshot()
    {
        lock (SyncRoot)
        {
            return new PartySnapshot(
                Id, Name, CreatedAt, InviteCode, _gold, _turn, IsPartyDefeated,
                Members.Select(m => new MemberSnapshot(m.Name, m.Class, m.Hp, m.MaxHp, m.IsDefeated || IsOut(m.Name))).ToArray(),
                LockStatus, _floor, _roomsCleared, _bossHp, _bossMaxHp);
        }
    }
}

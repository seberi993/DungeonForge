using DungeonForge.Domain.Rules;

namespace DungeonForge.Domain;

// Mutable aggregate shared via the party store. Mutators and Snapshot() lock SyncRoot internally (re-entrant, so handlers may hold
// it across a multi-step turn). Members can only be mutated through Party.
//
// Party is the one place a lock is taken. Its state lives in internal parts that have no lock of their own and are only reached
// while SyncRoot is held: ScenarioSlot (the encounter), TurnLocks (who has locked in), FloorState (floor and boss) and
// ChaosState (see Party.Chaos.cs and Party.Traitor.cs).
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
    private readonly ScenarioSlot _scenario = new();
    private readonly TurnLocks _locks = new();
    private readonly FloorState _progress = new();

    public Lock SyncRoot { get; } = new();

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    public string InviteCode { get; }

    public int Gold { get { lock (SyncRoot) { return _gold; } } }

    public int Turn { get { lock (SyncRoot) { return _turn; } } }

    public int Floor { get { lock (SyncRoot) { return _progress.Floor; } } }

    public int RoomsCleared { get { lock (SyncRoot) { return _progress.RoomsCleared; } } }

    public int BossHp { get { lock (SyncRoot) { return _progress.BossHp; } } }

    public int BossMaxHp { get { lock (SyncRoot) { return _progress.BossMaxHp; } } }

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

    public bool IsPartyDefeated { get { lock (SyncRoot) { return Members.All(m => !IsInPlay(m)); } } }

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

    // Hurts one named member outside the turn loop (a penalty), without touching the turn. Returns the damage applied.
    public int DamageMember(string name, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        lock (SyncRoot)
        {
            var member = Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && !m.IsDefeated);
            var applied = member?.TakeDamage(amount) ?? 0;
            SettleVaultIfWiped();
            return applied;
        }
    }

    // Moves the purse by a delta (a fine or a windfall outside the turn loop); it never goes below zero.
    public void AdjustGold(int delta)
    {
        lock (SyncRoot)
        {
            _gold = Math.Max(0, _gold + delta);
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

    // ---- The encounter: a scenario being narrated, then a stored one the party acts on ----

    public PendingEncounter? Pending { get { lock (SyncRoot) { return _scenario.Pending; } } }

    // Room of the scenario being generated (rolled in C#); null when idle or a scenario is already pending.
    public RoomType? AwaitingRoom { get { lock (SyncRoot) { return _scenario.AwaitingRoom; } } }

    // Claims the right to generate the next scenario. False when one is pending or already being generated, which
    // guarantees a single narrator call per turn.
    public bool TryBeginScenario(RoomType room)
    {
        lock (SyncRoot)
        {
            return _scenario.TryBegin(room, _turn);
        }
    }

    // Compare-and-set completion: only the first writer for the claimed turn wins (client submit vs. server deadline).
    public bool TryCompleteScenario(int turn, PendingEncounter encounter)
    {
        lock (SyncRoot)
        {
            if (!_scenario.Awaits(turn))
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
            _locks.Clear();
            return _scenario.Complete(encounter);
        }
    }

    public void AbortScenario()
    {
        lock (SyncRoot)
        {
            _scenario.Abort();
        }
    }

    // One action per member per turn: a resubmit replaces the earlier one in place. False when no encounter is pending.
    public bool TrySubmitAction(PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (SyncRoot)
        {
            return _scenario.TrySubmit(action);
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
            if (!_scenario.TrySubmit(action))
            {
                return false;
            }

            _locks.Lock(action);
            return true;
        }
    }

    public bool Unlock(Guid memberId)
    {
        lock (SyncRoot)
        {
            var removed = _locks.Release(memberId);
            if (removed)
            {
                _scenario.RemoveFromDiscussion(memberId);
            }

            return removed;
        }
    }

    public int LockedCount { get { lock (SyncRoot) { return _locks.Count; } } }

    // Defeated members cannot act, so they never hold the party up.
    public int RequiredLocks { get { lock (SyncRoot) { return Math.Max(1, Members.Count(IsInPlay)); } } }

    public bool AllLockedIn { get { lock (SyncRoot) { return _scenario.Pending is not null && _locks.Count >= RequiredLocks; } } }

    public IReadOnlyList<MemberLockStatus> LockStatus { get { lock (SyncRoot) { return _locks.Status(Members); } } }

    // ---- Resolving a turn ----

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
            _scenario.ClearPending();
            return applied;
        }
    }

    // Records a resolved room; see FloorState.Advance.
    public FloorProgress AdvanceProgress(RoomType room, int bossDamage)
    {
        lock (SyncRoot)
        {
            return _progress.Advance(room, bossDamage, Members);
        }
    }

    public PartySnapshot Snapshot()
    {
        lock (SyncRoot)
        {
            return new PartySnapshot(
                Id, Name, CreatedAt, InviteCode, _gold, _turn, IsPartyDefeated,
                Members.Select(m => new MemberSnapshot(m.Name, m.Class, m.Hp, m.MaxHp, !IsInPlay(m))).ToArray(),
                LockStatus, _progress.Floor, _progress.RoomsCleared, _progress.BossHp, _progress.BossMaxHp,
                _setup.Count, SetupPending);
        }
    }
}

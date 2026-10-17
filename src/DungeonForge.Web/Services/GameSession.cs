using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public sealed partial class GameSession
{
    private readonly List<LogEntry> _log = [];
    private List<MemberDto> _members = [];

    // Stable per browser session; identifies this player's submitted action on the server.
    public Guid PlayerId { get; } = Guid.NewGuid();
    // The hero this browser plays: the creator's first hero, or the one a joiner picked.
    public string? MyName { get; private set; }
    private MemberDto? Me => _members.FirstOrDefault(m => m.Name == MyName) ?? _members.FirstOrDefault();
    // What the server calls a player whose name is blank (the Api's ExploreRequestLimits.DefaultActionName).
    public const string DefaultPlayerName = "Adventurer";

    public string PlayerName => Me?.Name ?? DefaultPlayerName;
    public Guid PartyId { get; private set; }
    public string InviteCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public IReadOnlyList<MemberDto> Members => _members;
    public int TotalHp => _members.Sum(m => m.Hp);
    public int TotalMaxHp => _members.Sum(m => m.MaxHp);
    public int Gold { get; private set; }
    public int Turn { get; private set; }
    // Dungeon depth; the boss is awake (BossMaxHp > 0) once the floor's ordinary rooms are cleared.
    public int Floor { get; private set; } = 1;
    public int BossHp { get; private set; }
    public int BossMaxHp { get; private set; }
    public bool IsDefeated { get; private set; }
    public bool HasParty => PartyId != Guid.Empty;
    public IReadOnlyList<LogEntry> Log => _log;

    public void Start(PartyResponse party)
    {
        Clear();
        PartyId = party.PartyId;
        InviteCode = party.InviteCode;
        Name = party.Name;
        _members = [.. party.Members];
    }

    public string? Scenario { get; private set; }
    // Narrator's art tag for the current scenario; null falls back to the room type's art.
    public string? Scene { get; private set; }
    public bool ScenarioPending { get; private set; }
    public string? LastOutcome { get; private set; }
    // Where the latest outcome came from: a NarrationSources value (a browser's local model, the server, or canned fallback text).
    public string? NarrationSource { get; private set; }
    public IReadOnlyList<PlayerAction> Discussion { get; private set; } = [];
    public IReadOnlyList<LockStatusDto> LockStatus { get; private set; } = [];
    public int LockedCount => LockStatus.Count(s => s.LockedIn);
    public RoomType? PendingRoom { get; private set; }
    // The last resolved turn, acted out on the stage; null when it had no per-member actions (older servers).
    public StageShow? Show { get; private set; }
    public CharacterClass PlayerClass => Me?.Class ?? CharacterClass.Warrior;

    public bool Apply(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn <= Turn) return false;
        Turn = result.Turn;
        _reservedMana = 0;
        _mana = Math.Min(MaxMana, Mana + ManaRules.RegenPerTurn);
        Tally(result);
        var finishedScenario = Scenario;
        var finishedScene = Scene;
        ClearScenario();
        LastOutcome = result.LastOutcome;
        NarrationSource = null;
        Discussion = [];
        LockStatus = [];
        PendingRoom = null;
        NarrationPending = result.NarrationPending && result.LastOutcome is null;
        _narrationTurn = result.Turn;
        Gold = result.Gold;
        Floor = result.Floor;
        BossHp = result.BossHp;
        BossMaxHp = result.BossMaxHp;
        IsDefeated = result.IsPartyDefeated;
        ScenarioPending = !IsDefeated;
        if (result.Hits is { Count: > 0 } hits)
        {
            foreach (var hit in hits) ApplyDamage(hit.Name, hit.Damage);
        }
        else
        {
            ApplyDamage(result.TargetName, result.Damage);
        }

        Show = result.Beats is { Count: > 0 } beats
            ? new StageShow(result.Turn, result.RoomType, finishedScene, beats, result.Success, result.TargetName, result.Damage, result.GoldFound, result.Rolls)
            : null;
        _log.Add(new LogEntry(result.Turn, result.RoomType, result.TargetName, result.Damage, result.GoldFound, result.FlavorEvent, result.LastOutcome, finishedScenario));
        if (_earlyNarration is { } early && early.Turn == result.Turn && NarrationPending)
        {
            LastOutcome = early.Outcome;
            _log[^1] = _log[^1] with { Outcome = early.Outcome };
            NarrationPending = false;
            Scenario = early.NextScenario;
            Scene = early.NextScene;
            if (early.NextRoomType is { } earlyRoom) PendingRoom = earlyRoom;
            ScenarioPending = false;
        }
        _earlyNarration = null;
        Changed?.Invoke();
        return true;
    }

    // The ability the local player plans to use on the turn they lock in (null = none). Cleared when the turn moves on.
    public string? SelectedAbilityId { get; set; }

    public event Action? Changed;

    public void Join(PartyStateDto party, string? myName = null)
    {
        Clear();
        MyName = myName;
        PartyId = party.PartyId;
        InviteCode = party.InviteCode;
        Name = party.Name;
        _members = [.. party.Members];
        Gold = party.Gold;
        Turn = party.Turn;
        ApplyDepth(party);
        IsDefeated = party.IsPartyDefeated;
        LockStatus = party.LockStatus is null ? [] : [.. party.LockStatus];
    }

    private void ApplyDepth(PartyStateDto party)
    {
        Floor = party.Floor;
        BossHp = party.BossHp;
        BossMaxHp = party.BossMaxHp;
    }

    // Ignores broadcasts that are not strictly newer than what we already show (out-of-order delivery).
    public bool ApplyPartyState(PartyStateDto party)
    {
        if (party.PartyId != PartyId || party.Turn < Turn) return false;
        if (party.Turn == Turn) return ApplyRoster(party);
        _members = [.. party.Members];
        Gold = party.Gold;
        Turn = party.Turn;
        ApplyDepth(party);
        ClearScenario();
        Discussion = [];
        IsDefeated = party.IsPartyDefeated;
        LockStatus = party.LockStatus is null ? [] : [.. party.LockStatus];
        PendingRoom = null;
        Changed?.Invoke();
        return true;
    }

    // Same-turn broadcast (e.g. someone joined): refresh who is in the party without touching the running scene,
    // so every player's stage shows the same heroes.
    private bool ApplyRoster(PartyStateDto party)
    {
        var members = party.Members;
        // Only joins count here; HP is trusted from resolved turns, not same-turn rebroadcasts.
        if (members.Count <= _members.Count) return false;
        _members = [.. members];
        if (party.LockStatus is not null) LockStatus = [.. party.LockStatus];
        Changed?.Invoke();
        return true;
    }

    public bool ApplyRemote(ExploreResponse result) => Apply(result);

    public void MarkDefeated()
    {
        _members = [.. _members.Select(m => m with { Hp = 0, IsDefeated = true })];
        IsDefeated = true;
        ClearScenario();
    }

    public void Clear()
    {
        PartyId = Guid.Empty;
        InviteCode = "";
        Name = "";
        _members = [];
        MyName = null;
        Gold = 0;
        Turn = 0;
        Floor = 1;
        BossHp = BossMaxHp = 0;
        IsDefeated = false;
        _mana = null;
        _reservedMana = 0;
        _spells = [];
        _passes.Clear();
        _roomsWon = _roomsLost = _crits = _fumbles = _worstHit = 0;
        _worstHitName = null;
        _log.Clear();
        _private.Clear();
        ClearScenario();
        LastOutcome = null;
        NarrationSource = null;
        Discussion = [];
        LockStatus = [];
        PendingRoom = null;
        Show = null;
        NarrationPending = false;
        _narrationTurn = 0;
        _earlyNarration = null;
    }

    private void ApplyDamage(string? targetName, int damage)
    {
        if (targetName is null || damage <= 0) return;
        var index = _members.FindIndex(m => m.Name == targetName);
        if (index < 0) return;
        var hp = Math.Max(0, _members[index].Hp - damage);
        _members[index] = _members[index] with { Hp = hp, IsDefeated = hp == 0 };
    }
}

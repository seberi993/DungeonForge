using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public sealed class GameSession
{
    private readonly List<LogEntry> _log = [];
    private List<MemberDto> _members = [];

    // Stable per browser session; identifies this player's submitted action on the server.
    public Guid PlayerId { get; } = Guid.NewGuid();
    // The hero this browser plays: the creator's first hero, or the one a joiner picked.
    public string? MyName { get; private set; }
    private MemberDto? Me => _members.FirstOrDefault(m => m.Name == MyName) ?? _members.FirstOrDefault();
    public string PlayerName => Me?.Name ?? "Adventurer";
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
    // Where the latest outcome came from: "model" (a browser's local model), "server", or "fallback" (canned text).
    public string? NarrationSource { get; private set; }
    public IReadOnlyList<PlayerAction> Discussion { get; private set; } = [];
    public IReadOnlyList<LockStatusDto> LockStatus { get; private set; } = [];
    public int LockedCount => LockStatus.Count(s => s.LockedIn);
    public RoomType? PendingRoom { get; private set; }
    // The last resolved turn, acted out on the stage; null when it had no per-member actions (older servers).
    public StageShow? Show { get; private set; }
    public CharacterClass PlayerClass => Me?.Class ?? CharacterClass.Warrior;

    // ---- Mana: a client-side estimate for this player's hero only (the server has no mana pool). ----

    public const int ManaRegenPerTurn = 10;

    public static int MaxManaFor(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => 20,
        CharacterClass.Paladin => 40,
        CharacterClass.Bard => 50,
        CharacterClass.Druid => 60,
        CharacterClass.Priest => 60,
        CharacterClass.Necromancer => 70,
        CharacterClass.Mage => 80,
        _ => 40
    };

    private int? _mana;
    private int _reservedMana;
    private IReadOnlyList<SpellDto> _spells = [];

    public int MaxMana => MaxManaFor(PlayerClass);
    public int Mana => Math.Clamp(_mana ?? MaxMana, 0, MaxMana);

    public bool HasSpells => _spells.Count > 0;

    public void SetSpells(IReadOnlyList<SpellDto> spells) => _spells = [.. spells];

    // Spends the cost of the (longest-named) spell mentioned in the action; a later lock-in replaces an earlier one.
    public int ReserveMana(string actionText)
    {
        ReleaseMana();
        var spell = _spells
            .Where(s => actionText.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Name.Length)
            .FirstOrDefault();
        if (spell is null) return 0;

        var before = Mana;
        _mana = Math.Max(0, before - spell.ManaCost);
        _reservedMana = before - _mana.Value;
        Changed?.Invoke();
        return spell.ManaCost;
    }

    public void ReleaseMana()
    {
        if (_reservedMana == 0) return;
        _mana = Math.Min(MaxMana, Mana + _reservedMana);
        _reservedMana = 0;
        Changed?.Invoke();
    }

    // ---- Chaos mode: private whispers/secrets addressed to this player only, newest last. ----

    public const int MaxPrivateMessages = 5;
    private readonly List<PrivateMessageDto> _private = [];
    public IReadOnlyList<PrivateMessageDto> PrivateMessages => _private;

    public void AddPrivateMessage(PrivateMessageDto message)
    {
        if (string.IsNullOrWhiteSpace(message.Text)) return;
        _private.Add(message);
        if (_private.Count > MaxPrivateMessages) _private.RemoveAt(0);
        Changed?.Invoke();
    }

    public bool ApplyLockStatus(Guid partyId, IReadOnlyList<LockStatusDto> status)
    {
        if (partyId != PartyId) return false;
        LockStatus = [.. status];
        Changed?.Invoke();
        return true;
    }
    public bool NarrationPending { get; private set; }
    public bool HasScenario => Scenario is not null;
    private int _narrationTurn;
    private (int Turn, string Outcome, string? NextScenario, RoomType? NextRoomType, string? NextScene)? _earlyNarration;
    private const string NarrationPlaceholder = "The dungeon falls quiet; the tale of this moment is lost to the mist.";

    public bool ApplyDiscussion(Guid partyId, IReadOnlyList<PlayerAction> actions)
    {
        if (partyId != PartyId) return false;
        Discussion = [.. actions];
        Changed?.Invoke();
        return true;
    }

    // Own text (if any) replaces this player's shared action; teammates' non-blank shared actions are kept.
    public IReadOnlyList<PlayerAction> BuildActions(string? ownText)
    {
        var own = ownText?.Trim();
        var actions = Discussion
            .Where(a => !string.IsNullOrWhiteSpace(a.Text) && (a.MemberId != PlayerId || string.IsNullOrEmpty(own)))
            .ToList();
        if (!string.IsNullOrEmpty(own)) actions.Add(new PlayerAction(PlayerId, PlayerName, own));
        return actions;
    }

    // Matches by turn; narration landing before the resolve response is held until that turn is applied.
    public bool ApplyNarration(Guid partyId, int turn, string outcome, string? nextScenario = null, RoomType? nextRoomType = null, string? nextScene = null, string? source = null)
    {
        if (partyId != PartyId) return false;
        if (turn == Turn && !string.IsNullOrEmpty(outcome)) NarrationSource = source;
        if (string.IsNullOrEmpty(outcome)) return ApplyScenarioOnly(turn, nextScenario, nextRoomType, nextScene);
        var index = _log.FindIndex(e => e.Turn == turn);
        if (index < 0)
        {
            if (turn <= Turn) return false;
            _earlyNarration = (turn, outcome, nextScenario, nextRoomType, nextScene);
            return false;
        }

        _log[index] = _log[index] with { Outcome = outcome };
        if (turn == Turn)
        {
            LastOutcome = outcome;
            Scenario = nextScenario;
            Scene = nextScene;
            if (nextRoomType is { } nextRoom) PendingRoom = nextRoom;
            ScenarioPending = false;
        }
        if (turn == _narrationTurn) NarrationPending = false;
        Changed?.Invoke();
        return true;
    }

    // Empty-outcome broadcast (first-turn scenario): only fills a missing scenario for the current turn; never logs an outcome.
    private bool ApplyScenarioOnly(int turn, string? nextScenario, RoomType? nextRoomType, string? nextScene)
    {
        if (turn != Turn || nextScenario is null || Scenario is not null) return false;
        Scenario = nextScenario;
        Scene = nextScene;
        if (nextRoomType is { } scenarioRoom) PendingRoom = scenarioRoom;
        ScenarioPending = false;
        Changed?.Invoke();
        return true;
    }

    // Gives up waiting for narration and/or the next scenario so the player can re-prompt.
    public bool ExpireNarration(int turn)
    {
        var narrationWaiting = NarrationPending && _narrationTurn == turn;
        var scenarioWaiting = ScenarioPending && Turn == turn;
        if (!narrationWaiting && !scenarioWaiting) return false;
        if (narrationWaiting)
        {
            NarrationPending = false;
            LastOutcome = NarrationPlaceholder;
            var index = _log.FindIndex(e => e.Turn == turn);
            if (index >= 0) _log[index] = _log[index] with { Outcome = NarrationPlaceholder };
        }
        ScenarioPending = false;
        Changed?.Invoke();
        return true;
    }

    // Prompt/discussion step does not advance the turn, so it bypasses the strictly-newer guard (stale turns still rejected).
    public bool ApplyPrompt(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn < Turn) return false;
        if (result.Scenario is null && !result.ScenarioPending) return false;
        Scenario = result.Scenario;
        ScenarioPending = result.Scenario is null;
        if (result.Discussion is not null) Discussion = [.. result.Discussion];
        PendingRoom = result.RoomType;
        if (result.LockStatus is not null) LockStatus = [.. result.LockStatus];
        Changed?.Invoke();
        return true;
    }

    private void ClearScenario()
    {
        Scenario = null;
        Scene = null;
        ScenarioPending = false;
    }

    public bool Apply(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn <= Turn) return false;
        Turn = result.Turn;
        _reservedMana = 0;
        _mana = Math.Min(MaxMana, Mana + ManaRegenPerTurn);
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

    // ---- Run tally for the end-of-run screen; built only from turns this browser saw resolve. ----

    private readonly Dictionary<string, int> _passes = [];
    private int _roomsWon, _roomsLost, _crits, _fumbles, _worstHit;
    private string? _worstHitName;

    public RunSummary Summary
    {
        get
        {
            // First-listed wins a tie: the party order is stable and a plain max-by would pick an arbitrary dictionary entry.
            var mvp = _members.Select(m => m.Name).Where(_passes.ContainsKey).OrderByDescending(n => _passes[n]).FirstOrDefault();
            return new RunSummary(Turn, Gold, _roomsWon, _roomsLost, _crits, _fumbles,
                mvp, mvp is null ? 0 : _passes[mvp], _worstHitName, _worstHit,
                string.IsNullOrWhiteSpace(LastOutcome) || LastOutcome == NarrationPlaceholder ? null : LastOutcome);
        }
    }

    private void Tally(ExploreResponse result)
    {
        if (result.Success) _roomsWon++; else _roomsLost++;
        foreach (var roll in result.Rolls ?? [])
        {
            if (roll.Roll < roll.Chance) _passes[roll.Name] = _passes.GetValueOrDefault(roll.Name) + 1;
            if (roll.Crit) _crits++;
            if (roll.Fumble) _fumbles++;
        }

        foreach (var hit in result.Hits ?? [])
        {
            if (hit.Damage <= _worstHit) continue;
            _worstHit = hit.Damage;
            _worstHitName = hit.Name;
        }
    }

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

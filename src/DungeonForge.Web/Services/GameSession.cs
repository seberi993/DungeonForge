using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public sealed class GameSession
{
    private readonly List<LogEntry> _log = [];
    private List<MemberDto> _members = [];

    // Stable per browser session; identifies this player's submitted action on the server.
    public Guid PlayerId { get; } = Guid.NewGuid();
    public string PlayerName => _members.Count > 0 ? _members[0].Name : "Adventurer";
    public Guid PartyId { get; private set; }
    public string InviteCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public IReadOnlyList<MemberDto> Members => _members;
    public int TotalHp => _members.Sum(m => m.Hp);
    public int TotalMaxHp => _members.Sum(m => m.MaxHp);
    public int Gold { get; private set; }
    public int Turn { get; private set; }
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
    public bool ScenarioPending { get; private set; }
    public string? LastOutcome { get; private set; }
    public IReadOnlyList<PlayerAction> Discussion { get; private set; } = [];
    public IReadOnlyList<LockStatusDto> LockStatus { get; private set; } = [];
    public int LockedCount => LockStatus.Count(s => s.LockedIn);
    public RoomType? PendingRoom { get; private set; }
    public CharacterClass PlayerClass => _members.Count > 0 ? _members[0].Class : CharacterClass.Warrior;

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
    private (int Turn, string Outcome, string? NextScenario, RoomType? NextRoomType)? _earlyNarration;
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
    public bool ApplyNarration(Guid partyId, int turn, string outcome, string? nextScenario = null, RoomType? nextRoomType = null)
    {
        if (partyId != PartyId) return false;
        if (string.IsNullOrEmpty(outcome)) return ApplyScenarioOnly(turn, nextScenario, nextRoomType);
        var index = _log.FindIndex(e => e.Turn == turn);
        if (index < 0)
        {
            if (turn <= Turn) return false;
            _earlyNarration = (turn, outcome, nextScenario, nextRoomType);
            return false;
        }

        _log[index] = _log[index] with { Outcome = outcome };
        if (turn == Turn)
        {
            LastOutcome = outcome;
            Scenario = nextScenario;
            if (nextRoomType is { } nextRoom) PendingRoom = nextRoom;
            ScenarioPending = false;
        }
        if (turn == _narrationTurn) NarrationPending = false;
        Changed?.Invoke();
        return true;
    }

    // Empty-outcome broadcast (first-turn scenario): only fills a missing scenario for the current turn; never logs an outcome.
    private bool ApplyScenarioOnly(int turn, string? nextScenario, RoomType? nextRoomType)
    {
        if (turn != Turn || nextScenario is null || Scenario is not null) return false;
        Scenario = nextScenario;
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
        ScenarioPending = false;
    }

    public bool Apply(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn <= Turn) return false;
        Turn = result.Turn;
        ClearScenario();
        LastOutcome = result.LastOutcome;
        Discussion = [];
        LockStatus = [];
        PendingRoom = null;
        NarrationPending = result.NarrationPending && result.LastOutcome is null;
        _narrationTurn = result.Turn;
        Gold = result.Gold;
        IsDefeated = result.IsPartyDefeated;
        ScenarioPending = !IsDefeated;
        ApplyDamage(result.TargetName, result.Damage);
        _log.Add(new LogEntry(result.Turn, result.RoomType, result.TargetName, result.Damage, result.GoldFound, result.FlavorEvent, result.LastOutcome));
        if (_earlyNarration is { } early && early.Turn == result.Turn && NarrationPending)
        {
            LastOutcome = early.Outcome;
            _log[^1] = _log[^1] with { Outcome = early.Outcome };
            NarrationPending = false;
            Scenario = early.NextScenario;
            if (early.NextRoomType is { } earlyRoom) PendingRoom = earlyRoom;
            ScenarioPending = false;
        }
        _earlyNarration = null;
        Changed?.Invoke();
        return true;
    }

    public event Action? Changed;

    public void Join(PartyStateDto party)
    {
        Clear();
        PartyId = party.PartyId;
        InviteCode = party.InviteCode;
        Name = party.Name;
        _members = [.. party.Members];
        Gold = party.Gold;
        Turn = party.Turn;
        IsDefeated = party.IsPartyDefeated;
        LockStatus = party.LockStatus is null ? [] : [.. party.LockStatus];
    }

    // Ignores broadcasts that are not strictly newer than what we already show (out-of-order delivery).
    public bool ApplyPartyState(PartyStateDto party)
    {
        if (party.PartyId != PartyId || party.Turn <= Turn) return false;
        _members = [.. party.Members];
        Gold = party.Gold;
        Turn = party.Turn;
        ClearScenario();
        Discussion = [];
        IsDefeated = party.IsPartyDefeated;
        LockStatus = party.LockStatus is null ? [] : [.. party.LockStatus];
        PendingRoom = null;
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
        Gold = 0;
        Turn = 0;
        IsDefeated = false;
        _log.Clear();
        ClearScenario();
        LastOutcome = null;
        Discussion = [];
        LockStatus = [];
        PendingRoom = null;
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

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

    public string? PendingPrompt { get; private set; }
    public IReadOnlyList<string> PendingChoices { get; private set; } = [];
    public string? LastOutcome { get; private set; }
    public IReadOnlyList<PlayerAction> Discussion { get; private set; } = [];
    public bool NarrationPending { get; private set; }
    private int _narrationTurn;
    private (int Turn, string Outcome)? _earlyNarration;
    private const string NarrationPlaceholder = "The dungeon falls quiet; the tale of this moment is lost to the mist.";

    public bool ApplyDiscussion(Guid partyId, IReadOnlyList<PlayerAction> actions)
    {
        if (partyId != PartyId) return false;
        Discussion = [.. actions];
        Changed?.Invoke();
        return true;
    }

    // Matches by turn; narration landing before the resolve response is held until that turn is applied.
    public bool ApplyNarration(Guid partyId, int turn, string outcome)
    {
        if (partyId != PartyId) return false;
        var index = _log.FindIndex(e => e.Turn == turn);
        if (index < 0)
        {
            if (turn <= Turn) return false;
            _earlyNarration = (turn, outcome);
            return false;
        }

        _log[index] = _log[index] with { Outcome = outcome };
        if (turn == Turn) LastOutcome = outcome;
        if (turn == _narrationTurn) NarrationPending = false;
        Changed?.Invoke();
        return true;
    }

    public bool ExpireNarration(int turn)
    {
        if (!NarrationPending || _narrationTurn != turn) return false;
        NarrationPending = false;
        LastOutcome = NarrationPlaceholder;
        var index = _log.FindIndex(e => e.Turn == turn);
        if (index >= 0) _log[index] = _log[index] with { Outcome = NarrationPlaceholder };
        Changed?.Invoke();
        return true;
    }

    public bool HasPendingChoice => PendingPrompt is not null && PendingChoices.Count > 0;

    // Prompt step does not advance the turn, so it bypasses the strictly-newer guard (stale turns still rejected).
    public bool ApplyPrompt(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn < Turn) return false;
        if (result.ChoicePrompt is null || result.Choices is not { Count: > 0 }) return false;
        PendingPrompt = result.ChoicePrompt;
        PendingChoices = [.. result.Choices];
        if (result.Discussion is not null) Discussion = [.. result.Discussion];
        Changed?.Invoke();
        return true;
    }

    public void CancelPending()
    {
        ClearPending();
        Changed?.Invoke();
    }

    private void ClearPending()
    {
        PendingPrompt = null;
        PendingChoices = [];
    }

    public bool Apply(ExploreResponse result)
    {
        if (result.PartyId != PartyId || result.Turn <= Turn) return false;
        Turn = result.Turn;
        ClearPending();
        LastOutcome = result.LastOutcome;
        Discussion = [];
        NarrationPending = result.NarrationPending && result.LastOutcome is null;
        _narrationTurn = result.Turn;
        Gold = result.Gold;
        IsDefeated = result.IsPartyDefeated;
        ApplyDamage(result.TargetName, result.Damage);
        _log.Add(new LogEntry(result.Turn, result.RoomType, result.TargetName, result.Damage, result.GoldFound, result.FlavorEvent, result.LastOutcome));
        if (_earlyNarration is { } early && early.Turn == result.Turn && NarrationPending)
        {
            LastOutcome = early.Outcome;
            _log[^1] = _log[^1] with { Outcome = early.Outcome };
            NarrationPending = false;
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
    }

    // Ignores broadcasts that are not strictly newer than what we already show (out-of-order delivery).
    public bool ApplyPartyState(PartyStateDto party)
    {
        if (party.PartyId != PartyId || party.Turn <= Turn) return false;
        _members = [.. party.Members];
        Gold = party.Gold;
        Turn = party.Turn;
        ClearPending();
        Discussion = [];
        IsDefeated = party.IsPartyDefeated;
        Changed?.Invoke();
        return true;
    }

    public bool ApplyRemote(ExploreResponse result) => Apply(result);

    public void MarkDefeated()
    {
        _members = [.. _members.Select(m => m with { Hp = 0, IsDefeated = true })];
        IsDefeated = true;
        ClearPending();
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
        ClearPending();
        LastOutcome = null;
        Discussion = [];
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

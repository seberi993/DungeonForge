using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

// What the narrator has said: the outcome of the last turn, the next scenario, and narration that arrives out of order.
public sealed partial class GameSession
{
    public bool NarrationPending { get; private set; }
    public bool HasScenario => Scenario is not null;
    private int _narrationTurn;
    private (int Turn, string Outcome, string? NextScenario, RoomType? NextRoomType, string? NextScene)? _earlyNarration;
    private const string NarrationPlaceholder = "The dungeon falls quiet; the tale of this moment is lost to the mist.";

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
}

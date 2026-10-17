namespace DungeonForge.Api.Errors;

// Coded log events for failures that happen outside a request's reply: a broadcast, a leaderboard call, a narrator call,
// a timer. Each has a stable EventId and EventName so a log line can be traced to its method; the operation, party and
// turn are in the scope (see LogScope). Ranges: 2000s best-effort sends, 2100s leaderboard, 2200s narration.
// Grep the id or name to find the call site.
public static partial class LogEvents
{
    public const int BroadcastFailedId = 2001;
    public const int ScoreBoardReadFailedId = 2101;
    public const int ScoreRecordFailedId = 2102;
    public const int NarrationPhaseFailedId = 2201;
    public const int FallbackScenarioFailedId = 2202;
    public const int ScenarioSlotReleaseFailedId = 2203;
    public const int DeadlineNarrationFailedId = 2204;
    public const int NarratorEmptyScenarioId = 2211;
    public const int NarratorScenarioFailedId = 2212;
    public const int NarratorTurnFailedId = 2213;

    [LoggerMessage(
        EventId = BroadcastFailedId, EventName = "BroadcastFailed", Level = LogLevel.Warning,
        Message = "Best-effort send of {What} failed; the request carried on.")]
    public static partial void BroadcastFailed(this ILogger logger, Exception exception, string what);

    [LoggerMessage(
        EventId = ScoreBoardReadFailedId, EventName = "ScoreBoardReadFailed", Level = LogLevel.Warning,
        Message = "High score read failed; answering with an empty board.")]
    public static partial void ScoreBoardReadFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = ScoreRecordFailedId, EventName = "ScoreRecordFailed", Level = LogLevel.Warning,
        Message = "Could not record the finished run's score; the turn was not blocked.")]
    public static partial void ScoreRecordFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = NarrationPhaseFailedId, EventName = "NarrationPhaseFailed", Level = LogLevel.Warning,
        Message = "Narration phase failed; completing the turn with fallback text.")]
    public static partial void NarrationPhaseFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = FallbackScenarioFailedId, EventName = "FallbackScenarioFailed", Level = LogLevel.Warning,
        Message = "Fallback scenario could not be stored; aborting the scenario slot.")]
    public static partial void FallbackScenarioFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = ScenarioSlotReleaseFailedId, EventName = "ScenarioSlotReleaseFailed", Level = LogLevel.Warning,
        Message = "Could not release the scenario slot.")]
    public static partial void ScenarioSlotReleaseFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = DeadlineNarrationFailedId, EventName = "DeadlineNarrationFailed", Level = LogLevel.Warning,
        Message = "Server narration after the client deadline failed.")]
    public static partial void DeadlineNarrationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = NarratorEmptyScenarioId, EventName = "NarratorEmptyScenario", Level = LogLevel.Warning,
        Message = "Narrator returned an empty scenario; using the fallback.")]
    public static partial void NarratorEmptyScenario(this ILogger logger);

    [LoggerMessage(
        EventId = NarratorScenarioFailedId, EventName = "NarratorScenarioFailed", Level = LogLevel.Warning,
        Message = "Narrator failed generating the scenario; using the fallback.")]
    public static partial void NarratorScenarioFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = NarratorTurnFailedId, EventName = "NarratorTurnFailed", Level = LogLevel.Warning,
        Message = "Narrator failed narrating the turn; using the fallback.")]
    public static partial void NarratorTurnFailed(this ILogger logger, Exception exception);
}

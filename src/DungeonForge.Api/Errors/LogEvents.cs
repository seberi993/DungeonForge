namespace DungeonForge.Api.Errors;

// Coded log events for failures that happen outside a request's reply: a broadcast, a leaderboard read, a timer.
// Each has a stable EventId and EventName so a log line can be traced to its method. Ranges: 2000s best-effort sends,
// 2100s leaderboard. Grep the id or name to find the call site.
public static partial class LogEvents
{
    public const int BroadcastFailedId = 2001;
    public const int ScoreBoardReadFailedId = 2101;

    [LoggerMessage(
        EventId = BroadcastFailedId, EventName = "BroadcastFailed", Level = LogLevel.Warning,
        Message = "Best-effort send of {What} failed; the request carried on.")]
    public static partial void BroadcastFailed(this ILogger logger, Exception exception, string what);

    [LoggerMessage(
        EventId = ScoreBoardReadFailedId, EventName = "ScoreBoardReadFailed", Level = LogLevel.Warning,
        Message = "High score read failed; answering with an empty board.")]
    public static partial void ScoreBoardReadFailed(this ILogger logger, Exception exception);
}

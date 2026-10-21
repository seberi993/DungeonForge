using DungeonForge.Application.Errors;

namespace DungeonForge.Application.Features.Scores.GetHighScores;

public sealed class GetHighScoresHandler(IScoreBoard board, ILogger<GetHighScoresHandler> logger)
{
    public const string Operation = "Query:GetHighScores";
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public async Task<HighScoresResponse> HandleAsync(int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        try
        {
            var top = await board.TopAsync(take, ct);
            return new HighScoresResponse([.. top.Select((s, i) =>
                new HighScoreDto(i + 1, s.PartyName, s.Heroes, s.Floor, s.Turns, s.Gold, s.AchievedAt))]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A dead leaderboard must not break the lobby; it just shows empty.
            using var scope = LogScope.Begin(logger, Operation);
            logger.ScoreBoardReadFailed(ex);
            return new HighScoresResponse([]);
        }
    }
}

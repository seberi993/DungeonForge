namespace DungeonForge.Application.Features.Scores.GetHighScores;

public sealed record HighScoreDto(int Rank, string PartyName, IReadOnlyList<string> Heroes, int Floor, int Turns, int Gold, DateTimeOffset AchievedAt);

public sealed record HighScoresResponse(IReadOnlyList<HighScoreDto> Scores);

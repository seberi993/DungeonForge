namespace DungeonForge.Application.Features.Scores;

// One finished run. Id is the party id, so recording the same run twice is an overwrite, never a duplicate.
public sealed record RunScore(
    Guid Id,
    string PartyName,
    IReadOnlyList<string> Heroes,
    int Floor,
    int Turns,
    int Gold,
    DateTimeOffset AchievedAt)
{
    // Deeper floor wins, then more gold, then more turns survived; oldest wins a full tie.
    public static IEnumerable<RunScore> Rank(IEnumerable<RunScore> scores) =>
        scores.OrderByDescending(s => s.Floor).ThenByDescending(s => s.Gold)
              .ThenByDescending(s => s.Turns).ThenBy(s => s.AchievedAt);
}

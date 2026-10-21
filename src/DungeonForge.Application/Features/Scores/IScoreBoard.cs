namespace DungeonForge.Application.Features.Scores;

public interface IScoreBoard
{
    Task RecordAsync(RunScore score, CancellationToken ct = default);

    Task<IReadOnlyList<RunScore>> TopAsync(int limit, CancellationToken ct = default);
}

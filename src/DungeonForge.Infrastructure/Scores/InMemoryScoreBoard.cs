using DungeonForge.Domain;
using DungeonForge.Application.Features.Scores;
namespace DungeonForge.Infrastructure.Scores;

// Default board when Supabase is not configured. Process-lifetime only, capped so it can never grow unbounded.
public sealed class InMemoryScoreBoard : IScoreBoard
{
    public const int Capacity = 100;

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, RunScore> _scores = [];

    public Task RecordAsync(RunScore score, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _scores[score.Id] = score;
            if (_scores.Count > Capacity)
            {
                var worst = RunScore.Rank(_scores.Values).Last();
                _scores.Remove(worst.Id);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunScore>> TopAsync(int limit, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<RunScore>>([.. RunScore.Rank(_scores.Values).Take(limit)]);
        }
    }
}

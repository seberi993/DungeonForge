using DungeonForge.Domain;
using DungeonForge.Application.Errors;

namespace DungeonForge.Application.Features.Parties.ExploreDungeon;

// Command: lock answers in without the 409. It answers the prompt (with the lock status) until the last member locks in,
// and that last lock resolves the turn.
public sealed class LockInHandler(GetScenarioHandler scenario, ResolveTurnHandler resolveTurn)
{
    public async Task<Result<ExploreOutcome>> HandleAsync(Guid id, IReadOnlyList<PlayerAction> actions, CancellationToken ct)
    {
        var prompt = await scenario.HandleAsync(id, ExploreOperations.LockIn, ct);
        if (prompt.IsFailure || prompt.Value.Response.ScenarioPending)
        {
            return prompt;
        }

        var resolved = await resolveTurn.HandleAsync(id, actions, ExploreOperations.LockIn);

        // Members still missing is not a conflict here: the caller just gets the prompt back.
        return resolved.IsSuccess && resolved.Value.Waiting is not null ? new ExploreOutcome(resolved.Value.Response) : resolved;
    }
}

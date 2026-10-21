using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Abilities;

namespace DungeonForge.Application.Features.Parties.ExploreDungeon;

// POST /explore is one route that does three things, chosen by the body (see ExploreOperations.For):
//   no actions           -> GetScenarioHandler (Query: present or re-present the scenario)
//   actions + submitOnly -> LockInHandler (Command: lock in; the last lock resolves the turn)
//   actions              -> ResolveTurnHandler (Command: lock in, answer waiting until all are locked, then resolve)
// This class only routes. It is the endpoint's one dependency, so the route and its body contract do not change.
public sealed class ExploreDungeonHandler(
    GetScenarioHandler getScenario,
    LockInHandler lockIn,
    ResolveTurnHandler resolveTurn,
    SocialTurnPipeline? social = null)
{
    public Task<Result<ExploreOutcome>> HandleAsync(Guid id, ExploreRequest? request, CancellationToken ct = default)
    {
        social?.RecordIntents(id, request?.Actions, request?.Abilities);
        return request?.Actions is not { Count: > 0 } actions
            ? getScenario.HandleAsync(id, ExploreOperations.GetScenario, ct)
            : request.SubmitOnly
                ? lockIn.HandleAsync(id, actions, ct)
                : resolveTurn.HandleAsync(id, actions, ExploreOperations.ResolveTurn);
    }
}

using DungeonForge.Api.Domain;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public enum RoomType
{
    Combat,
    Chest,
    Puzzle,
}

public static class ExploreRequestLimits
{
    public const int MaxActions = 8;
    public const int MaxActionTextLength = 200;
    public const int MaxActionNameLength = 40;

    // Null when valid.
    public static string? Validate(IReadOnlyList<PlayerAction>? actions)
    {
        if (actions is null)
        {
            return null;
        }

        if (actions.Count > MaxActions)
        {
            return $"At most {MaxActions} actions are allowed.";
        }

        foreach (var action in actions)
        {
            if (action is null || action.MemberId == Guid.Empty)
            {
                return "Each action needs a member id.";
            }

            if (NarrationText.Clean(action.Text, MaxActionTextLength + 1).Length == 0)
            {
                return "Action text must not be empty.";
            }

            if (action.Text.Length > MaxActionTextLength)
            {
                return $"Action text must be at most {MaxActionTextLength} characters.";
            }

            if (action.Name is { Length: > MaxActionNameLength })
            {
                return $"Action name must be at most {MaxActionNameLength} characters.";
            }
        }

        return null;
    }
}

// Optional body. No actions (or an empty body) = ask for the pending scenario; actions = resolve the turn with the
// party's aggregated free-text plan; actions + SubmitOnly = share tactical input with the party (no turn change).
public sealed record ExploreRequest(IReadOnlyList<PlayerAction>? Actions = null, bool SubmitOnly = false);

// Prompt step: Scenario set, Turn unchanged, no deltas. When the next scenario is still being narrated, Scenario is
// null and ScenarioPending is true (no extra LLM call is made; NarrationReady delivers the text). Resolve step: deltas
// applied, LastOutcome null and NarrationPending true; outcome and next scenario follow as a NarrationReady event.
public sealed record ExploreResponse(
    Guid PartyId,
    int Turn,
    RoomType RoomType,
    string? TargetName,
    int Damage,
    int GoldFound,
    string FlavorEvent,
    int Gold,
    bool IsPartyDefeated,
    string? Scenario = null,
    string? LastOutcome = null,
    IReadOnlyList<PlayerAction>? Discussion = null,
    bool NarrationPending = false,
    bool ScenarioPending = false);

// Phase two of a resolved turn. Turn matches ExploreResponse.Turn of the resolve response. NextScenario/NextRoomType are
// null when the party was defeated (there is no next encounter).
public sealed record NarrationReady(
    Guid PartyId,
    int Turn,
    string Outcome,
    string? NextScenario = null,
    RoomType? NextRoomType = null);

public enum ExploreStatus
{
    Explored,
    NotFound,
    Defeated,
    InvalidRequest,
}

public readonly record struct ExploreResult(ExploreStatus Status, ExploreResponse? Response = null, string? Error = null);

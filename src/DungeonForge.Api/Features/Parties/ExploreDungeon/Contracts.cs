using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties;
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

// Optional body. No actions (or an empty body) = ask for the pending scenario;
// actions = lock those members in and answer 409 "waiting" until every member is locked (then the turn resolves);
// actions + SubmitOnly = lock in without the 409: answers the prompt with lock status, or the resolved turn on the last lock.
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
    bool ScenarioPending = false,
    IReadOnlyList<LockStatusDto>? LockStatus = null,
    IReadOnlyList<ActionBeat>? Beats = null,
    bool Success = false,
    IReadOnlyList<HitDto>? Hits = null,
    NarrationBrief? Brief = null,
    IReadOnlyList<RollDto>? Rolls = null);

// One member's d100 roll against their chance (pass when Roll &lt; Chance). Crit/Fumble are the extremes. Presentational for the dice UI.
public sealed record RollDto(string Name, int Roll, int Chance, bool Crit, bool Fumble);

// Handed only to the request that triggered a narration slot when client narration is on: the prompts the browser's
// local model should run. Kind is "scenario" (plain prose) or "turn" (JSON). The reply goes back via POST .../narration.
public sealed record NarrationBrief(int Turn, string Kind, string System, string User, string? JsonSchema, int MaxTokens);

// One member's own damage from a failed action this turn. TargetName/Damage on the response mirror the first hit.
public sealed record HitDto(string Name, int Damage);

// What one member does on screen when the turn resolves; the client animates these in order. Purely presentational.
public enum ActionVerb
{
    Strike,
    Cast,
    Stab,
    Heal,
}

public sealed record ActionBeat(string Name, CharacterClass Class, ActionVerb Verb, string Text);

// Phase two of a resolved turn. Turn matches ExploreResponse.Turn of the resolve response. NextScenario/NextRoomType are
// null when the party was defeated (there is no next encounter).
public sealed record NarrationReady(
    Guid PartyId,
    int Turn,
    string Outcome,
    string? NextScenario = null,
    RoomType? NextRoomType = null,
    string? NextScene = null,
    string? Source = null);

public enum ClientNarrationStatus
{
    Accepted,
    NotFound,
    Conflict,
}

public enum ExploreStatus
{
    Explored,
    NotFound,
    Defeated,
    InvalidRequest,
    Waiting,
}

// Waiting: not every member has locked in yet (HTTP 409). Waiting carries the 409 body.
public readonly record struct ExploreResult(
    ExploreStatus Status,
    ExploreResponse? Response = null,
    string? Error = null,
    WaitingForPartyResponse? Waiting = null);

// 409 body of a direct resolve while members are still missing.
public sealed record WaitingForPartyResponse(
    Guid PartyId,
    int Locked,
    int Required,
    IReadOnlyList<LockStatusDto> LockStatus,
    string Status = "waiting");

using DungeonForge.Api.Domain;

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

            if (string.IsNullOrWhiteSpace(action.Text))
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

// Optional body. Index null + Actions = submit tactical input to the pending encounter (no turn change);
// both null = ask for (or re-read) the pending encounter; index 0..2 = resolve (Actions are submitted first).
public sealed record ExploreRequest(int? SelectedChoiceIndex, IReadOnlyList<PlayerAction>? Actions = null);

// Prompt step: ChoicePrompt/Choices set, Turn unchanged, no deltas. Resolve step: deltas applied, LastOutcome null and
// NarrationPending true; the text follows later as a NarrationReady event.
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
    string? ChoicePrompt = null,
    IReadOnlyList<string>? Choices = null,
    string? LastOutcome = null,
    IReadOnlyList<PlayerAction>? Discussion = null,
    bool NarrationPending = false);

// Phase two of a resolved turn. Turn matches ExploreResponse.Turn of the resolve response.
public sealed record NarrationReady(Guid PartyId, int Turn, string Outcome);

public enum ExploreStatus
{
    Explored,
    NotFound,
    Defeated,
    InvalidChoice,
}

public readonly record struct ExploreResult(ExploreStatus Status, ExploreResponse? Response = null, string? Error = null);

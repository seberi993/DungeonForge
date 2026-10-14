namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public enum RoomType
{
    Combat,
    Chest,
}

// Optional body. Null index = ask for (or re-read) the pending encounter; 0..2 = resolve the chosen option.
public sealed record ExploreRequest(int? SelectedChoiceIndex);

// Prompt step: ChoicePrompt/Choices set, Turn unchanged, no deltas. Resolve step: LastOutcome set, deltas applied.
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
    string? LastOutcome = null);

public enum ExploreStatus
{
    Explored,
    NotFound,
    Defeated,
    InvalidChoice,
}

public readonly record struct ExploreResult(ExploreStatus Status, ExploreResponse? Response = null, string? Error = null);

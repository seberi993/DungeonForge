namespace DungeonForge.Application.Features.Parties.SubmitNarration;

// The browser's local-model text for the turn it was briefed on. Declined (or blank text) hands the turn to the server narrator.
public sealed record SubmitNarrationRequest(int Turn, string? Text = null, bool Declined = false);

public static class SubmitNarrationLimits
{
    // Generous: a scenario is capped at 1400 characters and a turn reply carries two texts plus JSON syntax.
    public const int MaxTextLength = 4000;
}

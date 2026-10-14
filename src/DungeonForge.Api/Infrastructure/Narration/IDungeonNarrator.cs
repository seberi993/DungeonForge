using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public sealed record NarrationContext(RoomType Room, string PartyName, int Turn, bool PartyWounded)
{
    // Every member's sanitized input for this turn, in submission order. Empty outside the resolve step.
    public IReadOnlyList<PlayerAction> Actions { get; init; } = [];
}

// Options are ordered safest to riskiest.
public sealed record EncounterChoice(string Prompt, IReadOnlyList<string> Options);

public interface IDungeonNarrator
{
    Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct);

    Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct);
}

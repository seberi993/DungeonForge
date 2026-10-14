using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public sealed record NarrationContext(RoomType Room, string PartyName, int Turn, bool PartyWounded);

// Options are ordered safest to riskiest.
public sealed record EncounterChoice(string Prompt, IReadOnlyList<string> Options);

public interface IDungeonNarrator
{
    Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct);

    Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct);
}

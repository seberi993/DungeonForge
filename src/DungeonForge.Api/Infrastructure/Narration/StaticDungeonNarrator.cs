namespace DungeonForge.Api.Infrastructure.Narration;

public sealed class StaticDungeonNarrator : IDungeonNarrator
{
    public Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct) =>
        Task.FromResult(FallbackFlavor.Choices(ctx.Room, ctx.Turn));

    public Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct) =>
        Task.FromResult(FallbackFlavor.Outcome(ctx.Room, success, ctx.Turn));
}

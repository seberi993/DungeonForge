namespace DungeonForge.Api.Infrastructure.Narration;

public sealed class StaticDungeonNarrator : IDungeonNarrator
{
    public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
        Task.FromResult(FallbackFlavor.Scenario(ctx.Room, ctx.Turn));

    public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct) =>
        Task.FromResult(FallbackFlavor.Complete(null, success, ctx));
}

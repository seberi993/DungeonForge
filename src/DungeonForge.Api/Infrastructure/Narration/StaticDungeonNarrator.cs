namespace DungeonForge.Api.Infrastructure.Narration;

public sealed class StaticDungeonNarrator : IDungeonNarrator
{
    public Task<string> NarrateAsync(NarrationContext ctx, CancellationToken ct) =>
        Task.FromResult(FallbackFlavor.Pick(ctx.Room, ctx.Turn));
}

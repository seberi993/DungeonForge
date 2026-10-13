using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public sealed record NarrationContext(RoomType Room, string PartyName, int Turn, bool PartyWounded);

public interface IDungeonNarrator
{
    Task<string> NarrateAsync(NarrationContext ctx, CancellationToken ct);
}

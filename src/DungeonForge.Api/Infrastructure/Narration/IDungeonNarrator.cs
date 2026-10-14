using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public sealed record NarrationContext(RoomType Room, string PartyName, int Turn, bool PartyWounded)
{
    // Every member's sanitized input for this turn, in submission order. Empty outside the resolve step.
    public IReadOnlyList<PlayerAction> Actions { get; init; } = [];

    // Members whose own action failed this turn, and heroes now at 0 HP; flavor input only, the rolls happened in C#.
    public IReadOnlyList<string> Failed { get; init; } = [];
    public IReadOnlyList<string> Downed { get; init; } = [];

    // Dungeon depth of the resolved room, and whether this turn felled the floor's boss (flavor input only).
    public int Floor { get; init; } = 1;
    public bool FloorCleared { get; init; }

    // Room rolled in C# for the next encounter; null when there is none (party defeated) or outside the resolve step.
    public RoomType? NextRoom { get; init; }
}

// Outcome of the resolved turn plus the next scenario, produced by ONE model call. Next is empty when no next room exists.
public sealed record TurnNarration(string Outcome, string Next, string NextScene = "")
{
    // True when canned flavor filled in for the model (quota exhausted, bad reply), so the UI can say so.
    public bool FromFallback { get; init; }
}

public interface IDungeonNarrator
{
    // First turn of a party only: a rich, open-ended scenario for ctx.Room ending in an open question.
    Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct);

    // Every later turn: outcome narration for the resolved turn AND the next scenario (ctx.NextRoom) in a single call.
    Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct);
}

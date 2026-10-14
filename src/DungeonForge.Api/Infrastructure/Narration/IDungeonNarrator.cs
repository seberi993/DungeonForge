using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public sealed record RosterEntry(string Name, CharacterClass Class);

// A short private rumor for one player. Target is a member name or a class name.
public sealed record Whisper(string Target, string Text)
{
    public string Format() => $"Whisper to {Target}: '{Text}'";
}

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

    // Who is in the party (for targeted whispers), set dressing to weave in, and treachery already resolved in C# that the
    // narrator should describe as just exposed. All flavor input only.
    public IReadOnlyList<RosterEntry> Roster { get; init; } = [];
    public IReadOnlyList<string> Motifs { get; init; } = [];
    public IReadOnlyList<string> Treachery { get; init; } = [];
}

// Outcome of the resolved turn plus the next scenario, produced by ONE model call. Next is empty when no next room exists.
public sealed record TurnNarration(string Outcome, string Next, string NextScene = "")
{
    // True when canned flavor filled in for the model (quota exhausted, bad reply), so the UI can say so.
    public bool FromFallback { get; init; }

    // Private whispers generated alongside the narration; delivered only to their targets, never broadcast.
    public IReadOnlyList<Whisper> Whispers { get; init; } = [];

    // Value equality across the whisper list (the synthesized record equality would compare it by reference).
    public bool Equals(TurnNarration? other) =>
        other is not null && Outcome == other.Outcome && Next == other.Next && NextScene == other.NextScene
        && FromFallback == other.FromFallback && Whispers.SequenceEqual(other.Whispers);

    public override int GetHashCode() => HashCode.Combine(Outcome, Next, NextScene, FromFallback, Whispers.Count);
}

public interface IDungeonNarrator
{
    // First turn of a party only: a rich, open-ended scenario for ctx.Room ending in an open question.
    Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct);

    // Every later turn: outcome narration for the resolved turn AND the next scenario (ctx.NextRoom) in a single call.
    Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct);
}

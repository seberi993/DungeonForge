using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Domain;

// Choices are ordered safest to riskiest; the index maps to a success-chance tier in C#.
// Discussion holds at most one PlayerAction per member, in submission order.
public sealed record PendingEncounter(
    RoomType Room,
    string Prompt,
    IReadOnlyList<string> Choices,
    IReadOnlyList<PlayerAction>? Discussion = null);

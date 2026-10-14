using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Domain;

// Choices are ordered safest to riskiest; the index maps to a success-chance tier in C#.
public sealed record PendingEncounter(RoomType Room, string Prompt, IReadOnlyList<string> Choices);

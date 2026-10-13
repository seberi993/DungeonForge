namespace DungeonForge.Api.Features.Parties;

internal sealed record Party(Guid Id, string Name, IReadOnlyList<string> Members, DateTimeOffset CreatedAt);

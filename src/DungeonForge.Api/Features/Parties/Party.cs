namespace DungeonForge.Api.Features.Parties;

internal sealed record Party(
    Guid Id,
    string Name,
    IReadOnlyList<string> Members,
    DateTimeOffset CreatedAt,
    int Hp = 20,
    int Gold = 0,
    int Turn = 0,
    string InviteCode = "");

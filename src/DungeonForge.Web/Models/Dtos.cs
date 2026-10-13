namespace DungeonForge.Web.Models;

public enum RoomType
{
    Combat,
    Chest
}

public sealed record CreatePartyRequest(string Name);

public sealed record PartyResponse(Guid PartyId, string Name, IReadOnlyList<string> Members, DateTimeOffset CreatedAt);

public sealed record ExploreResponse(
    Guid PartyId,
    int Turn,
    RoomType RoomType,
    int HpChange,
    int GoldFound,
    string FlavorEvent,
    int Hp,
    int Gold);

public sealed record LogEntry(int Turn, RoomType RoomType, int HpChange, int GoldFound, string FlavorEvent);

public sealed record PartyStateDto(
    Guid PartyId,
    string Name,
    IReadOnlyList<string> Members,
    DateTimeOffset CreatedAt,
    int Hp,
    int Gold,
    int Turn);

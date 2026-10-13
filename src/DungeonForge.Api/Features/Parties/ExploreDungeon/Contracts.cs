namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public enum RoomType
{
    Combat,
    Chest,
}

public sealed record ExploreResponse(
    Guid PartyId,
    int Turn,
    RoomType RoomType,
    int HpChange,
    int GoldFound,
    string FlavorEvent,
    int Hp,
    int Gold);

public enum ExploreStatus
{
    Explored,
    NotFound,
    Defeated,
}

public readonly record struct ExploreResult(ExploreStatus Status, ExploreResponse? Response = null);

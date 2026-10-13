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
    string? TargetName,
    int Damage,
    int GoldFound,
    string FlavorEvent,
    int Gold,
    bool IsPartyDefeated);

public enum ExploreStatus
{
    Explored,
    NotFound,
    Defeated,
}

public readonly record struct ExploreResult(ExploreStatus Status, ExploreResponse? Response = null);

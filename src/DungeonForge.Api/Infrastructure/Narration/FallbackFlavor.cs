using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public static class FallbackFlavor
{
    private static readonly string[] Combat =
    [
        "A goblin ambush springs from the shadows.",
        "A skeleton guard rattles to life.",
        "A giant rat swarm pours from the walls.",
    ];

    private static readonly string[] Chest =
    [
        "A dusty chest creaks open, spilling coins.",
        "Behind a loose stone, a forgotten purse awaits.",
        "A gilded coffer glimmers in the torchlight.",
    ];

    public static string Pick(RoomType room, int turn)
    {
        var flavors = room == RoomType.Combat ? Combat : Chest;
        return flavors[Math.Abs(turn) % flavors.Length];
    }
}

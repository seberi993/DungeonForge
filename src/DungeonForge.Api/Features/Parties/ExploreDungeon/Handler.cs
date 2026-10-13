using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonHandler(IMemoryCache cache, IRoller roller)
{
    private static readonly string[] CombatFlavor =
    [
        "A goblin ambush springs from the shadows.",
        "A skeleton guard rattles to life.",
        "A giant rat swarm pours from the walls.",
    ];
    private static readonly string[] ChestFlavor =
    [
        "A dusty chest creaks open, spilling coins.",
        "Behind a loose stone, a forgotten purse awaits.",
        "A gilded coffer glimmers in the torchlight.",
    ];

    // Static: guards the cache read-modify-write regardless of handler lifetime (single-instance host).
    private static readonly Lock Gate = new();

    public ExploreResult Handle(Guid id)
    {
        var key = PartyCache.Key(id);
        lock (Gate)
        {
            if (!cache.TryGetValue(key, out Party? party) || party is null)
            {
                return new ExploreResult(ExploreStatus.NotFound);
            }

            if (party.Hp <= 0)
            {
                return new ExploreResult(ExploreStatus.Defeated);
            }

            var room = roller.Next(2) == 0 ? RoomType.Combat : RoomType.Chest;
            var hpChange = room == RoomType.Combat ? -(2 + party.Turn % 3) : 0;
            var goldFound = room == RoomType.Chest ? 5 + party.Turn : 0;
            var flavors = room == RoomType.Combat ? CombatFlavor : ChestFlavor;

            var hp = Math.Max(0, party.Hp + hpChange);
            var updated = party with { Hp = hp, Gold = party.Gold + goldFound, Turn = party.Turn + 1 };

            cache.Set(key, updated, PartyCache.EntryOptions());

            return new ExploreResult(ExploreStatus.Explored, new ExploreResponse(
                updated.Id, updated.Turn, room, hp - party.Hp, goldFound,
                flavors[party.Turn % flavors.Length], updated.Hp, updated.Gold));
        }
    }
}

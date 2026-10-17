using DungeonForge.Api.Domain.Rules;

namespace DungeonForge.Api.Domain;

// BossNext: the next room is the boss (woken or still standing). FloorCleared: the boss fell and the party descended.
public sealed record FloorProgress(bool BossNext, bool FloorCleared);

// Where the party is in the dungeon: floor, rooms cleared on it, and the boss once it wakes. Part of a Party: no lock of its
// own, the caller holds the party's SyncRoot.
internal sealed class FloorState
{
    public int Floor { get; private set; } = 1;

    public int RoomsCleared { get; private set; }

    public int BossHp { get; private set; }

    public int BossMaxHp { get; private set; }

    // Records a resolved room. An ordinary room counts towards the boss; the last one wakes it. In the boss room the
    // party's passed actions wear the boss down and, once it falls, the party heals and descends to the next floor.
    public FloorProgress Advance(RoomType room, int bossDamage, IReadOnlyList<PartyMember> members)
    {
        if (room != RoomType.Boss)
        {
            RoomsCleared++;
            var wakes = RoomsCleared >= PartyRules.RoomsPerFloor;
            if (wakes)
            {
                BossMaxHp = PartyRules.BossHpFor(Floor);
                BossHp = BossMaxHp;
            }

            return new FloorProgress(wakes, false);
        }

        BossHp = Math.Max(0, BossHp - bossDamage);
        if (BossHp > 0)
        {
            return new FloorProgress(true, false);
        }

        foreach (var m in members)
        {
            m.Heal(PartyRules.FloorHealFor(m.MaxHp));
        }

        Floor++;
        RoomsCleared = 0;
        BossMaxHp = 0;
        return new FloorProgress(false, true);
    }
}

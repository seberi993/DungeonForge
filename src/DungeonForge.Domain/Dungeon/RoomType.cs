namespace DungeonForge.Domain;

// The kinds of room a party can meet. The boss is never rolled: it is always entered when a floor's ordinary rooms run out.
public enum RoomType
{
    Combat,
    Chest,
    Puzzle,
    Boss,
}

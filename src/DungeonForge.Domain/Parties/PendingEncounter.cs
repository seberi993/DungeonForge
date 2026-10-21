
namespace DungeonForge.Domain;

// Scenario is the narrated, open-ended situation the party discusses; there are no predefined options.
// Discussion holds at most one PlayerAction per member, in submission order.
public sealed record PendingEncounter(
    RoomType Room,
    string Scenario,
    IReadOnlyList<PlayerAction>? Discussion = null);

using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

// What the explore slices tell a client about a party at rest, and the party facts the narrator is given.
internal static class ExploreResponses
{
    // The scenario is stored: its text, the lock status, no deltas.
    public static ExploreResponse Prompt(Party party, PendingEncounter pending) =>
        new(party.Id, party.Turn, pending.Room, null, 0, 0, pending.Scenario, party.Gold, party.IsPartyDefeated,
            Scenario: pending.Scenario, Discussion: null,
            LockStatus: PartyMapping.ToLockDtos(party.LockStatus));

    // The scenario is still being narrated: the text arrives via NarrationReady and the next prompt.
    public static ExploreResponse Awaiting(Party party, RoomType room) =>
        new(party.Id, party.Turn, room, null, 0, 0, string.Empty, party.Gold, party.IsPartyDefeated, ScenarioPending: true);

    public static IReadOnlyList<RosterEntry> Roster(Party party) =>
        [.. party.Members.Select(m => new RosterEntry(m.Name, m.Class))];

    public static bool IsWounded(Party party) =>
        RoomRules.IsWounded(party.Members.Sum(m => m.Hp), party.Members.Sum(m => m.MaxHp));
}

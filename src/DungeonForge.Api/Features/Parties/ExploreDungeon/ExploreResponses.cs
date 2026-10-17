using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

// What the explore slices tell a client about a party at rest, and the party facts the narrator is given.
internal static class ExploreResponses
{
    // The scenario is stored: its text, the lock status, no deltas.
    public static ExploreResponse Prompt(Party party, PendingEncounter pending) =>
        new(PartyId: party.Id, Turn: party.Turn, RoomType: pending.Room, TargetName: null, Damage: 0, GoldFound: 0,
            FlavorEvent: pending.Scenario, Gold: party.Gold, IsPartyDefeated: party.IsPartyDefeated,
            Scenario: pending.Scenario, LockStatus: PartyMapping.ToLockDtos(party.LockStatus));

    // The scenario is still being narrated: the text arrives via NarrationReady and the next prompt.
    public static ExploreResponse Awaiting(Party party, RoomType room) =>
        new(PartyId: party.Id, Turn: party.Turn, RoomType: room, TargetName: null, Damage: 0, GoldFound: 0,
            FlavorEvent: string.Empty, Gold: party.Gold, IsPartyDefeated: party.IsPartyDefeated, ScenarioPending: true);

    // A resolved turn: what each action rolled, who got hurt (TargetName and Damage mirror the first hit), what was found,
    // and where the party stands now. NarrationPending is left for the caller to set once the turn is announced.
    public static ExploreResponse Resolved(
        Party party, PendingEncounter pending, IReadOnlyList<PlayerAction> discussion, IReadOnlyList<DamageResult> hits,
        IReadOnlyList<RollDto> rolls, IReadOnlyList<ActionBeat> beats, int gold, bool success, bool floorCleared,
        IReadOnlyList<string>? combatLog)
    {
        var firstHit = hits.Count > 0 ? hits[0] : null;
        return new ExploreResponse(
            PartyId: party.Id, Turn: party.Turn, RoomType: pending.Room, TargetName: firstHit?.TargetName, Damage: firstHit?.Damage ?? 0,
            GoldFound: gold, FlavorEvent: pending.Scenario, Gold: party.Gold, IsPartyDefeated: party.IsPartyDefeated,
            Discussion: discussion, Beats: beats, Success: success, Hits: [.. hits.Select(h => new HitDto(h.TargetName, h.Damage))],
            Rolls: rolls, Floor: party.Floor, BossHp: party.BossHp, BossMaxHp: party.BossMaxHp, FloorCleared: floorCleared,
            CombatLog: combatLog);
    }

    public static IReadOnlyList<RosterEntry> Roster(Party party) =>
        [.. party.Members.Select(m => new RosterEntry(m.Name, m.Class))];

    public static bool IsWounded(Party party) =>
        RoomRules.IsWounded(party.Members.Sum(m => m.Hp), party.Members.Sum(m => m.MaxHp));
}

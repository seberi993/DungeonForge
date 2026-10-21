using DungeonForge.Domain;
using DungeonForge.Domain.Rules;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Abilities;
using DungeonForge.Application.Narration;

namespace DungeonForge.Application.Features.Parties.ExploreDungeon;

// The game math of one turn, in C# and under the party lock: lock the actions in, and once every member is locked, roll each
// action, apply damage, gold and floor progress, and roll the next room. It never talks to a client or a narrator; what it
// returns is what ResolveTurnHandler announces. The roller is consulted in a fixed order that scripted-roller tests depend on:
// confrontation tie-break, covert plans, one d100 per action, then the next room.
public sealed class TurnResolver(
    IPartyStore parties,
    IRoller roller,
    VocabularySelector vocabulary,
    SocialTurnPipeline? social = null)
{
    internal TurnResolution Resolve(Guid id, IReadOnlyList<PlayerAction> actions, string operation)
    {
        if (!parties.TryGet(id, out var party))
        {
            return TurnResolution.Failed(PartyErrors.NotFound(operation, id));
        }

        // Per-party lock: serializes mutation against Party.Snapshot() readers and concurrent resolves.
        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return TurnResolution.Failed(ExploreErrors.PartyDefeated(operation, id, party.Turn));
            }

            party.TryResolveConfrontation(roller);
            if (party.Confrontation is not null)
            {
                return TurnResolution.Failed(ExploreErrors.ConfrontationInProgress(operation, id, party.Turn));
            }

            if (party.Pending is null)
            {
                return TurnResolution.Failed(party.AwaitingRoom is null
                    ? ExploreErrors.NoActiveEncounter(operation, id, party.Turn)
                    : ExploreErrors.ScenarioPending(operation, id, party.Turn));
            }

            SubmitAll(party, actions);
            var pending = party.Pending!;
            if (pending.Discussion is not { Count: > 0 } discussion)
            {
                return TurnResolution.Failed(ExploreErrors.NoActions(operation, id));
            }

            if (!party.AllLockedIn)
            {
                parties.Save(party);
                var waiting = new WaitingForPartyResponse(
                    party.Id, party.LockedCount, party.RequiredLocks, PartyMapping.ToLockDtos(party.LockStatus));
                return TurnResolution.Waiting(
                    new ExploreOutcome(ExploreResponses.Prompt(party, pending), waiting), PartyMapping.ToResponse(party.Snapshot()));
            }

            return RollTurn(party, pending, discussion);
        }
    }

    // Every locked-in action rolls on its own, with odds shifted by what the rest of the party locked in.
    private TurnResolution RollTurn(Party party, PendingEncounter pending, IReadOnlyList<PlayerAction> discussion)
    {
        var turnBefore = party.Turn;
        var floorBefore = party.Floor;
        var chances = ActionOdds.For(discussion);

        // Hidden gifts nudge the same numbers (chance, failure damage, gold); they never replace the rolls.
        var plan = social?.Plan(party, discussion, chances, turnBefore);
        if (plan is not null)
        {
            chances = [.. Enumerable.Range(0, chances.Count).Select(plan.Chance)];
        }

        // Secret plans resolve first, in C#; a sabotaged spell fails no matter how well the roll goes.
        var covert = discussion.Any(a => a.Covert is not null)
            ? CovertResolver.Resolve(discussion, party.Members.Where(m => !party.IsBanished(m.Name)), roller)
            : CovertResolution.None;
        var failed = new List<DamageResult>();
        var passed = 0;
        var crits = 0;
        var bossDamage = 0;
        var rolls = new List<RollDto>();
        for (var i = 0; i < discussion.Count; i++)
        {
            var roll = roller.Next(100);
            var crit = ActionOdds.IsCrit(roll);
            var fumble = ActionOdds.IsFumble(roll);
            rolls.Add(new RollDto(discussion[i].Name, roll, chances[i], crit, fumble));
            if (roll < chances[i] && !covert.Sabotaged.Contains(discussion[i].Name))
            {
                passed++;
                if (crit) crits++;
                bossDamage += crit ? ActionOdds.BossCritDamage : ActionOdds.BossHitDamage;
                continue;
            }

            var damage = RoomRules.FailDamage(pending.Room, turnBefore, floorBefore);
            if (plan is not null) damage = plan.Reduce(i, damage);
            failed.Add(new DamageResult(discussion[i].Name, fumble ? ActionOdds.FumbleDamage(damage) : damage));
        }

        // The room itself needs a strict majority; a tie goes to the dungeon.
        var success = passed > failed.Count;
        var isBoss = pending.Room == RoomType.Boss;
        var bossFalls = isBoss && party.BossHp - bossDamage <= 0;
        success |= bossFalls;
        var roomPays = isBoss ? bossFalls : success;
        var gold = roomPays ? RoomRules.Gold(pending.Room, turnBefore, floorBefore) + crits * ActionOdds.CritGold + (plan?.GoldBonus ?? 0) : 0;

        // Chaos parties do not bank drops: gold and the boss's Cursed Idol wait in the Unclaimed Vault.
        var dropsWaitInVault = party.Modifiers.Chaos;
        var hits = party.ApplyMemberOutcomes([.. failed, .. covert.Hits], dropsWaitInVault ? 0 : gold);
        foreach (var (thief, amount) in covert.Heals) party.Heal(thief, amount);
        if (dropsWaitInVault && (gold > 0 || bossFalls)) party.DropVault(gold, bossFalls ? TraitorPact.CursedIdol : null);

        // The next room is rolled in C# before any narrator call; nothing follows a defeated party. A boss
        // that is awake (or just woken by the floor's last room) is always the next room.
        RoomType? nextRoom = null;
        var floorCleared = false;
        foreach (var action in discussion)
        {
            party.NoteStory(action.Text);
        }

        if (!party.IsPartyDefeated)
        {
            var progress = party.AdvanceProgress(pending.Room, bossDamage);
            floorCleared = progress.FloorCleared;
            nextRoom = progress.BossNext ? RoomType.Boss : RoomRules.Roll(roller, party.StoryLean);
            party.TryBeginScenario(nextRoom.Value);
        }

        // Mutated in place under the lock; re-Set refreshes the sliding expiry.
        parties.Save(party);

        // Keep the invite entry's sliding expiry in step with an active party.
        parties.Touch(party);

        var response = ExploreResponses.Resolved(
            party, pending, discussion, hits, rolls, ToBeats(party, discussion), gold, success, floorCleared,
            combatLog: covert.Outcomes.Count > 0 ? covert.Log : null);
        var context = new NarrationContext(pending.Room, party.Name, turnBefore, ExploreResponses.IsWounded(party))
        {
            Floor = floorBefore,
            FloorCleared = floorCleared,
            Actions = discussion,
            NextRoom = nextRoom,
            Failed = [.. failed.Select(f => f.TargetName)],
            Downed = [.. party.Members.Where(m => m.IsDefeated).Select(m => m.Name)],
            Roster = ExploreResponses.Roster(party),
            Motifs = vocabulary.Pick(),
            Treachery = covert.Log,
            SetupAnswers = party.SetupAnswers,
            StoryTags = party.StoryLean.Describe(),
            RecentOutcomes = party.RecentOutcomes,
        };

        return TurnResolution.Resolved(new ExploreOutcome(response), context, success, PartyMapping.ToResponse(party.Snapshot()));
    }

    private static void SubmitAll(Party party, IReadOnlyList<PlayerAction>? actions)
    {
        foreach (var action in actions ?? [])
        {
            var text = NarrationText.Clean(action.Text, ExploreRequestLimits.MaxActionTextLength);
            if (text.Length == 0)
            {
                continue;
            }

            var name = NarrationText.Sanitize(action.Name, ExploreRequestLimits.MaxActionNameLength, ExploreRequestLimits.DefaultActionName);
            party.TryLockIn(new PlayerAction(action.MemberId, name, text, CleanCovert(action.Covert)));
        }
    }

    private static CovertIntent? CleanCovert(CovertIntent? covert) =>
        covert is null ? null : covert with { Target = NarrationText.Clean(covert.Target, ExploreRequestLimits.MaxActionNameLength) };

    private static ActionBeat[] ToBeats(Party party, IReadOnlyList<PlayerAction> discussion) =>
        [.. discussion.Select(action =>
        {
            var characterClass = party.Members.FirstOrDefault(m => m.Name == action.Name)?.Class ?? CharacterClass.Warrior;
            return new ActionBeat(action.Name, characterClass, ActionVerbs.Pick(characterClass, action.Text), action.Text);
        })];
}

// What one attempt to resolve a turn came to. Failed: an error. Waiting: not every member is locked in yet (Snapshot is the
// party to announce). Resolved: the turn happened, with the narration context and whether the room succeeded.
internal sealed record TurnResolution(
    Result<ExploreOutcome> Outcome, NarrationContext? Context, bool Success, GetPartyStateResponse? Snapshot)
{
    public bool IsWaiting => Outcome.IsSuccess && Outcome.Value.Waiting is not null && Snapshot is not null;

    public bool IsResolved => Outcome.IsSuccess && Context is not null && Snapshot is not null;

    public static TurnResolution Failed(Error error) => new(error, null, false, null);

    public static TurnResolution Waiting(ExploreOutcome outcome, GetPartyStateResponse snapshot) =>
        new(outcome, null, false, snapshot);

    public static TurnResolution Resolved(ExploreOutcome outcome, NarrationContext context, bool success, GetPartyStateResponse snapshot) =>
        new(outcome, context, success, snapshot);
}

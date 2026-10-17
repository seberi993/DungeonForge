using System.Globalization;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public static class FallbackFlavor
{
    private static readonly string[] CombatScenarios =
    [
        "Three **goblins** crouch behind an overturned cart, spears levelled, while a **chieftain** drums a dented shield. What do you do?",
        "Skeletal guards claw out of the **dust**, jaws clicking in unison, and a **wight** waits past the toppled **statues**. What do you do?",
        "Something wet drags itself across the ceiling above a rope **bridge**, and the **chains** below begin to ring. What do you do?",
        "A swarm of **rats** parts around a single armoured **hound** that has clearly been waiting for you. What do you do?",
        "The **mist** thickens, and shapes with too many elbows step out of it, humming a lullaby. What do you do?",
    ];

    private static readonly string[] ChestScenarios =
    [
        "An iron-banded **chest** sits on a plinth above a black pool, its lock suspiciously clean. What do you do?",
        "A gilded **coffer** rests on rotting velvet, thin **wires** running from its hinges into the walls. What do you do?",
        "The **chest** is open, empty, and still warm, and a trail of wet footprints leads away from it. What do you do?",
        "A grinning **statue** holds a **key** in one hand and a lit **candle** in the other, and the chest below them hums. What do you do?",
        "The treasure is stacked in plain sight beside a sleeping **skeleton** whose hand grips a **lever**. What do you do?",
    ];

    private static readonly string[] PuzzleScenarios =
    [
        "A sealed **door** is ringed with glowing **runes**, and **sand** is already trickling from the ceiling. What do you do?",
        "A stone **sage** offers a silver bowl, and four pedestals around it each hold the wrong kind of object. What do you do?",
        "Three **levers** jut from the wall under a mural that someone has scratched out, badly. What do you do?",
        "A floor of mismatched **tiles** spells half a sentence, and the **mirror** at the end finishes it differently. What do you do?",
        "Water rises through a drain as a **dial** ticks toward a symbol none of you recognise. What do you do?",
    ];

    private static readonly string[] BossScenarios =
    [
        "A hulking **ogre** in scorched plate drags its **hammer** across the floor, a glowing seam splitting its chest. What do you do?",
        "A black **dragon** uncoils from a heap of melted **coins**, an old **ballista** still aimed at its wound. What do you do?",
        "The floor guardian is a **bell** the size of a house, and something inside it is ringing back. What do you do?",
        "A robed **warden** with a lantern for a head blocks the stair, and his shadow moves a second late. What do you do?",
        "Chains hold a monstrous **jailer** to the wall, but they are all in the process of coming loose. What do you do?",
    ];

    private static readonly string[] BossSuccess =
    [
        "The guardian staggers and crashes down at last. The way deeper is open, which you will no doubt find a way to regret.",
        "The final blow lands and the monster collapses. A miracle, and one the dungeon will remember with contempt.",
    ];

    private static readonly string[] BossFailure =
    [
        "The boss shrugs off the assault and answers with something heavy. Still standing, which is more than the plan managed.",
        "Your attack falters and the guardian presses its advantage. It is hardly even trying.",
    ];

    private static readonly string[] CombatSuccess =
    [
        "Somehow your nerve holds and the foe falls back. Don't let it go to your heads; it was a low bar.",
        "A decisive blow ends the fight. Pure luck, but sure, call it skill.",
    ];

    private static readonly string[] CombatFailure =
    [
        "The plan unravels and the enemy lands a painful strike. Shocking, truly.",
        "You misjudged the moment and paid for it in blood. Who could have seen that coming? Everyone.",
    ];

    private static readonly string[] ChestSuccess =
    [
        "The lid gives way and coins spill out. Congratulations on the accident.",
        "Your gamble pays off and the treasure is yours. Try not to look so smug about it.",
    ];

    private static readonly string[] ChestFailure =
    [
        "The mechanism jams and the treasure slips away. Greed, meet consequences.",
        "Nothing but dust and splinters for your trouble. Fitting.",
    ];

    private static readonly string[] PuzzleSuccess =
    [
        "The mechanism clicks and the way opens. A blind goat could have stumbled into it, but fine.",
        "Together you crack the riddle, and it only took the combined effort of all of you.",
    ];

    private static readonly string[] PuzzleFailure =
    [
        "The trap springs and a jolt of force sweeps through the party. Next time, try thinking first.",
        "A wrong move triggers the lock's defenses. Stone and sparks fly, and so does my patience.",
    ];

    // Deterministic, descriptive scenario used whenever the model is unavailable or returns nothing usable.
    public static string Scenario(RoomType room, int turn)
    {
        var set = room switch { RoomType.Combat => CombatScenarios, RoomType.Puzzle => PuzzleScenarios, RoomType.Boss => BossScenarios, _ => ChestScenarios };
        return set[Index(turn, set.Length)];
    }

    public static string Outcome(RoomType room, bool success, int turn)
    {
        var set = (room, success) switch
        {
            (RoomType.Combat, true) => CombatSuccess,
            (RoomType.Combat, false) => CombatFailure,
            (RoomType.Boss, true) => BossSuccess,
            (RoomType.Boss, false) => BossFailure,
            (RoomType.Puzzle, true) => PuzzleSuccess,
            (RoomType.Puzzle, false) => PuzzleFailure,
            (_, true) => ChestSuccess,
            _ => ChestFailure,
        };
        return set[Index(turn, set.Length)];
    }

    private static readonly string[] AttackJabs =
    [
        "{0} charges in swinging, because subtlety is for people with brains.",
        "{0} decided violence was a plan. Adorable.",
    ];

    private static readonly string[] EvadeJabs =
    [
        "{0} edges toward the exit, and honestly, the dungeon expected nothing braver.",
        "{0} tries to dodge the danger. Running: the one skill you all practise.",
    ];

    private static readonly string[] LootJabs =
    [
        "{0} lunges for the shiny thing, greed leading the way as always.",
        "{0} sees treasure and forgets every lesson ever learned. Riveting.",
    ];

    private static readonly string[] OtherJabs =
    [
        "{0} had an idea. Everyone is as surprised as you are.",
        "{0} chimed in with a plan, and the walls sighed.",
    ];

    // A scoff aimed at the first member idea, chosen by keyword; empty when nobody offered an idea.
    public static string Jab(IReadOnlyList<PlayerAction> actions, int turn)
    {
        var first = actions.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Text));
        if (first is null) return string.Empty;

        var set = ActionOdds.Classify(first.Text) switch
        {
            ActionKind.Attack => AttackJabs,
            ActionKind.Evade => EvadeJabs,
            ActionKind.Loot => LootJabs,
            _ => OtherJabs,
        };
        return string.Format(CultureInfo.InvariantCulture, set[Index(turn, set.Length)], first.Name);
    }

    private static readonly string[] WhisperTemplates =
    [
        "{0} took extra gold while you were looking away.",
        "{0} has smiled at your back for three rooms now. Ask yourself why.",
        "I saw {0} palm something from the last spoils. Say nothing. Watch.",
        "{0} asked the walls which of you would fall first. The walls named you.",
    ];

    // A private rumor for one member about another; needs at least two members. Deterministic per turn.
    public static IReadOnlyList<Whisper> Whispers(IReadOnlyList<RosterEntry> roster, int turn)
    {
        if (roster.Count < 2) return [];

        var target = roster[Index(turn, roster.Count)];
        var accused = roster[Index(turn + 1, roster.Count)];
        var text = string.Format(CultureInfo.InvariantCulture, WhisperTemplates[Index(turn, WhisperTemplates.Length)], accused.Name);
        return [new Whisper(target.Name, text)];
    }

    // Fills each blank part of a (possibly partial) model reply independently with deterministic flavor.
    public static TurnNarration Complete(TurnNarration? partial, bool success, NarrationContext ctx)
    {
        var filledOutcome = string.IsNullOrWhiteSpace(partial?.Outcome);
        var outcome = filledOutcome
            ? $"{Jab(ctx.Actions, ctx.Turn)} {Outcome(ctx.Room, success, ctx.Turn)}".Trim()
            : partial!.Outcome;
        var filledNext = ctx.NextRoom is not null && string.IsNullOrWhiteSpace(partial?.Next);
        var next = ctx.NextRoom is not { } room
            ? string.Empty
            : filledNext ? Scenario(room, ctx.Turn + 1) : partial!.Next;
        IReadOnlyList<Whisper> whispers = partial is { Whispers.Count: > 0 } ? partial.Whispers : Whispers(ctx.Roster, ctx.Turn);
        return new TurnNarration(outcome, next, partial?.NextScene ?? string.Empty)
        {
            FromFallback = filledOutcome || filledNext,
            Whispers = whispers,
        };
    }

    private static int Index(int turn, int length) => (int)(Math.Abs((long)turn) % length);
}

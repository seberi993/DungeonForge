using System.Globalization;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

public static class FallbackFlavor
{
    private static readonly string[] CombatScenarios =
    [
        "The corridor opens into a low, vaulted guardroom that smells of rust and old **smoke**. Three **goblins** crouch behind an overturned cart with crude spears levelled, while a scarred chieftain hammers a dented shield from a raised ledge at the back. A single iron **sconce** gutters overhead and leaves both flanks in deep shadow. A rotten **rope** bridge sags over a pit to the left, and stacked barrels line the right wall. The goblins have seen you, they are not retreating, and every heartbeat of hesitation lets them spread out. Your companions are tense and the doorway behind you is narrow. What do you do?",
        "A cracked flagstone hall stretches ahead, lined with toppled **statues** and pillars wrapped in dead ivy. Skeletal guards in rusted mail rise one by one from the **dust**, their jaws clicking in unison as a pale green light seeps from the empty eye sockets. At the far end a taller **wight** leans on a notched greatsword and waits for you to step into the open. The pillars offer cover but the floor between them is slick with something dark. Behind you the only retreat is a stairwell that groans under old weight. The dead are patient, and you are not. What do you do?",
    ];

    private static readonly string[] ChestScenarios =
    [
        "Beyond a collapsed archway lies a small, circular vault where **water** drips steadily from the ceiling into a black pool. On a stone plinth in the middle sits an iron-banded **chest**, its lid furred with green corrosion and its heavy lock gleaming oddly clean. Scratch marks ring the plinth, and a few brittle **bones** lie scattered nearby, one hand still clutching a bent lockpick. The pool laps close enough to soak anyone who reaches the chest, and something small ripples just beneath the surface. Whatever waits inside is worth the risk to someone, but the vault itself feels like a warning. What do you do?",
        "A narrow alcove behind a false wall hides a gilded **coffer** resting on a bed of rotting velvet. Thin **wires** run from its hinges into tiny holes in the walls, and the air carries a faint, sweet chemical scent. Two stone **gargoyles** flank the alcove, their mouths open as if ready to speak, and the floor in front of the coffer is made of slightly mismatched tiles. Faint scratching comes from somewhere above the ceiling. The treasure is within easy reach, which is exactly what makes your stomach tighten. Time and torchlight are both running down. What do you do?",
    ];

    private static readonly string[] PuzzleScenarios =
    [
        "A round chamber of black stone surrounds a sealed **door** carved with a ring of glowing **runes**. Each rune is set in its own movable tile, and the tiles hum faintly when touched. Above the door a mural shows a sun, a serpent, a crown, and a closed eye, arranged in a circle that does not quite match the runes below. Cracks in the ceiling release a slow trickle of **sand**, and the pile growing near the far wall suggests the room is filling. A worn inscription at the base warns that haste has buried many before you. The answer is here somewhere, if you can read it together. What do you do?",
        "You step into a gallery where a stone **statue** of a robed sage sits on a throne, one hand outstretched with a silver bowl. Four pedestals surround it, each holding a different object: a cracked mirror, a rusted **key**, a **candle** that burns without heat, and a bowl of dark water. Words are etched around the throne in an old tongue, and the floor tiles shift softly under your boots. The gate behind the statue is locked, and the bowl seems to be waiting for an offering. A low rumbling rolls through the walls whenever someone moves too quickly. The room rewards patience and punishes guesses. What do you do?",
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
        var set = room switch { RoomType.Combat => CombatScenarios, RoomType.Puzzle => PuzzleScenarios, _ => ChestScenarios };
        return set[Index(turn, set.Length)];
    }

    public static string Outcome(RoomType room, bool success, int turn)
    {
        var set = (room, success) switch
        {
            (RoomType.Combat, true) => CombatSuccess,
            (RoomType.Combat, false) => CombatFailure,
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
        return new TurnNarration(outcome, next, partial?.NextScene ?? string.Empty) { FromFallback = filledOutcome || filledNext };
    }

    private static int Index(int turn, int length) => (int)(Math.Abs((long)turn) % length);
}

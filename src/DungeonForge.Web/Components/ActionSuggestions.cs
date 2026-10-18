namespace DungeonForge.Web.Components;

// Faint, deliberately unhinged prompts shown in the empty action box.
public static class ActionSuggestions
{
    public static readonly IReadOnlyList<string> Pool =
    [
        "Befriend the nearest shadow and demand it pay rent",
        "Challenge the loot to a staring contest",
        "Serenade the monsters with a sea shanty",
        "Politely ask the trap to go off somewhere else",
        "Throw the smallest party member at the problem",
        "Lick the suspicious glowing mushroom for science",
        "Declare yourself mayor of this corridor",
        "Negotiate with the door. It looks reasonable",
        "Pretend to be a very convincing chandelier",
        "Hold a funeral for the last torch",
        "Dramatically monologue until something happens",
        "Adopt the scariest thing here as a pet",
        "Moonwalk past the danger with total confidence",
        "Offer the wall a hug and see what it does",
        "Bribe the darkness with a single sock",
        "Start a rumour that the dungeon is haunted",
        "Pick a fight with your own reflection",
        "Teach the goblins an interpretive dance",
        "Roll the party down the hallway like barrels",
        "Whisper sweet nothings to the treasure",
    ];

    public static string Pick(Random rng) => Pool[rng.Next(Pool.Count)];


    // A bright hue (0-359) so each suggestion shows up in its own colour.
    public static int PickHue(Random rng) => rng.Next(360);
}

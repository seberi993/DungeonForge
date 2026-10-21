namespace DungeonForge.Application.Narration;

// Keeps every run and every scenario from reading the same: a theme and tone that are stable for a party (derived from its
// name) plus a fresh twist per call. Flavor input only; the model is told to follow it, never to change a result.
public static class RunFlavor
{
    private static readonly string[] Themes =
    [
        "a drowned cathedral", "a collapsing mine", "an abandoned clockwork foundry", "a plague-ridden catacomb",
        "a mirrored labyrinth", "a sunken library", "a frozen prison", "a fungal cavern", "a ghost market under the earth",
        "a crumbling observatory", "a bone-white salt cave", "a haunted menagerie",
    ];

    private static readonly string[] Tones =
    [
        "dry gallows humor", "creeping dread", "absurd bureaucracy", "bitter melancholy", "feverish paranoia",
        "mock-heroic grandeur", "cold clinical detachment",
    ];

    private static readonly string[] Twists =
    [
        "something here is oddly polite", "a sound is missing that should be there", "the room is older than the dungeon",
        "someone has been here recently", "a smell that doesn't belong", "the architecture seems to watch back",
        "a harmless detail that feels wrong", "an unexpected tenant", "something is being counted", "a lie carved into the wall",
        "a door that shouldn't exist", "the light comes from the wrong place",
    ];

    public static string Theme(string partyName) => Themes[Index(partyName, Themes.Length)];

    public static string Tone(string partyName) => Tones[Index(partyName + "tone", Tones.Length)];

    public static string Twist(string partyName, int turn) => Twists[Index($"{partyName}#{turn}", Twists.Length)];

    // Prompt fragment appended to the user prompt; the twist changes every turn but is stable for a given turn.
    public static string Describe(string partyName, int turn) =>
        $" Run theme: {Theme(partyName)}. Tone: {Tone(partyName)}. Twist for this scene: {Twist(partyName, turn)}. " +
        "Invent fresh specifics; do not reuse the same creatures, props or opening words as a typical dungeon scene.";

    // Stable across processes (string.GetHashCode is randomized).
    private static int Index(string value, int length)
    {
        var hash = 17;
        foreach (var c in value)
        {
            hash = unchecked((hash * 31) + c);
        }

        return Math.Abs(hash % length);
    }
}

namespace DungeonForge.Api.Features.Parties;

// The DM's opening questions. Each member gets a different one (by roster position), so together the answers paint a scene.
public static class SetupQuestions
{
    public static readonly string[] All =
    [
        "Where does your party find itself, and what does the place smell, sound and feel like?",
        "What lurks in this place that everyone whispers about but no one has truly seen?",
        "Who are you really, and what do you carry that you would rather not show anyone?",
        "What are you after down here, and what did it already cost you to come this far?",
        "What old grudge, debt or promise ties your party to this place?",
        "What strange thing did you notice on the way in that the others missed?",
        "Whom did you leave behind, and what will you do if you never see them again?",
        "What is the one lie your party told to get here?",
    ];

    public static string For(int memberIndex) => All[memberIndex % All.Length];
}

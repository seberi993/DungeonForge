namespace DungeonForge.Api.Domain.Rules;

// What the players' own words lean toward, as keyword counts per rollable room kind: fighting, grabbing loot, or digging into
// mysteries. Tallied from the opening scene and every action; it only nudges the next room roll (see RoomRules.Roll).
public readonly record struct StoryLean(int Combat, int Chest, int Puzzle)
{
    public const int MaxSwapPercent = 40;
    public const int SwapPercentPerPoint = 10;

    public StoryLean Plus(StoryLean other) => new(Combat + other.Combat, Chest + other.Chest, Puzzle + other.Puzzle);

    // The room kind with a strictly higher count than both others; null on a tie or when nothing leans.
    public RoomType? Favored
    {
        get
        {
            if (Combat > Chest && Combat > Puzzle) return RoomType.Combat;
            if (Chest > Combat && Chest > Puzzle) return RoomType.Chest;
            if (Puzzle > Combat && Puzzle > Chest) return RoomType.Puzzle;
            return null;
        }
    }

    // Chance (percent) that the rolled room is swapped for the favored one: grows with the lead, never above the cap.
    public int SwapPercent
    {
        get
        {
            if (Favored is null) return 0;
            var top = Math.Max(Combat, Math.Max(Chest, Puzzle));
            var second = new[] { Combat, Chest, Puzzle }.OrderDescending().ElementAt(1);
            return Math.Min(MaxSwapPercent, (top - second) * SwapPercentPerPoint);
        }
    }

    // Leanings the narrator can name, once a kind has real weight behind it.
    public IReadOnlyList<string> Describe()
    {
        var tags = new List<string>();
        if (Combat >= 2) tags.Add("itching for a fight");
        if (Chest >= 2) tags.Add("greedy for loot");
        if (Puzzle >= 2) tags.Add("drawn to secrets and mysteries");
        return tags;
    }
}

// Scores free text for StoryLean by keyword stems. Pure; the same text always scores the same.
public static class StoryTags
{
    private static readonly string[] Combat =
        ["fight", "attack", "kill", "slay", "charge", "smash", "battle", "blood", "war", "monster", "sword", "hunt", "revenge", "burn"];

    private static readonly string[] Chest =
        ["gold", "loot", "treasure", "coin", "steal", "rich", "gem", "fortune", "debt", "pocket"];

    private static readonly string[] Puzzle =
        ["riddle", "puzzle", "rune", "book", "ancient", "secret", "mystery", "study", "clue", "ritual", "lore", "curious", "read"];

    public static StoryLean Score(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;

        var lower = text.ToLowerInvariant();
        return new StoryLean(Hits(lower, Combat), Hits(lower, Chest), Hits(lower, Puzzle));
    }

    private static int Hits(string text, string[] stems) => stems.Count(s => text.Contains(s, StringComparison.Ordinal));
}

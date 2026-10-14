namespace DungeonForge.Api.Infrastructure.Narration;

// Closed vocabulary the narrator picks from so the client can map a scene to hand-made ASCII art.
public static class SceneTags
{
    public static readonly IReadOnlyList<string> All =
        ["goblin", "skeleton", "rat", "dragon", "ghost", "chest", "door", "lever", "trap", "altar", "generic"];

    // Returns the lowercase tag when it is in the vocabulary; empty otherwise (client falls back to the room's art).
    public static string Normalize(string? value)
    {
        var tag = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return All.Contains(tag) ? tag : string.Empty;
    }
}

namespace DungeonForge.Api.Infrastructure.Narration;

// Order: GoogleFlash:ApiKey_0..N, Gemini:ApiKeys[], GEMINI_KEY_1..N, legacy GoogleFlash:ApiKey.
// The first source that yields any key wins. Blank and duplicate values are dropped.
public static class NarrationKeyResolver
{
    public static string[] Resolve(IConfiguration config)
    {
        string[][] sources =
        [
            Indexed(config, i => $"GoogleFlash:ApiKey_{i}", 0),
            config.GetSection("Gemini:ApiKeys").Get<string[]>() ?? [],
            Indexed(config, i => $"GEMINI_KEY_{i}", 1),
            [config["GoogleFlash:ApiKey"] ?? string.Empty],
        ];

        foreach (var source in sources)
        {
            var keys = source.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToArray();
            if (keys.Length > 0)
            {
                return keys;
            }
        }

        return [];
    }

    // Scans consecutive indices until one is entirely absent (a blank value still counts as present).
    private static string[] Indexed(IConfiguration config, Func<int, string> name, int start)
    {
        var keys = new List<string>();
        for (var i = start; config[name(i)] is { } value; i++)
        {
            keys.Add(value);
        }

        return [.. keys];
    }
}

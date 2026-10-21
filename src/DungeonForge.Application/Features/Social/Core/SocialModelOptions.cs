namespace DungeonForge.Application.Features.Social.Core;

// Limits for the model-backed parts of the social game (config section "Social:Model"). The model is optional: any
// failure or timeout falls back to the deterministic C# rules.
public sealed class SocialModelOptions
{
    public int ParserTimeoutMs { get; set; } = 8000;

    public int JudgeTimeoutMs { get; set; } = 10000;

    // At most one judge call per party in this window; later events inside it keep the rule-based outcome.
    public int JudgeIntervalSeconds { get; set; } = 20;

    public int JudgeQueueCapacity { get; set; } = 16;
}

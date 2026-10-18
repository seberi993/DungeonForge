namespace DungeonForge.Api.Features.Social.Core;

// Names of the per-run counters. Derived names (event:*, verdict:*) keep the supervision pipeline free of a counter per rule.
public static class SocialMetrics
{
    public const string Seeds = "seeds";
    public const string PeerAnswers = "peer_answers";
    public const string PeerRoundsResolved = "peer_rounds_resolved";
    public const string AsksSent = "asks_sent";
    public const string AsksCompelled = "asks_compelled";
    public const string AsksReplied = "asks_replied";
    public const string AbuseFlags = "abuse_flags";
    public const string RateLimited = "rate_limited";
    public const string LlmDecisions = "llm_decisions";
    public const string LlmFallbacks = "llm_fallbacks";

    public static string ForEvent(string type) => $"event:{type}";

    public static string ForVerdict(Verdict verdict) => $"verdict:{verdict.ToString().ToLowerInvariant()}";
}

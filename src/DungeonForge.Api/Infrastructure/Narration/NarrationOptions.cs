namespace DungeonForge.Api.Infrastructure.Narration;

public sealed class NarrationOptions
{
    public string? ApiKey { get; set; }
    public string[] ApiKeys { get; set; } = [];
    public string Model { get; set; } = "gemini-flash-lite-latest";
    public int TimeoutSeconds { get; set; } = 8;
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public bool Enabled { get; set; }
    public double Temperature { get; set; } = 0.85;

    // When on, the player's browser writes the narration (WebLLM) and submits it; the server narrator only steps in
    // when nobody delivers within ClientDeadlineSeconds or the client declines.
    public bool ClientNarration { get; set; }
    public double ClientDeadlineSeconds { get; set; } = 40;
}

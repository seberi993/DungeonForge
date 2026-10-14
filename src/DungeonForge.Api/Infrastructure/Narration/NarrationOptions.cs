namespace DungeonForge.Api.Infrastructure.Narration;

public sealed class NarrationOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-haiku-5-5";
    public int TimeoutSeconds { get; set; } = 5;
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public bool Enabled { get; set; }
    public double Temperature { get; set; } = 0.85;
}

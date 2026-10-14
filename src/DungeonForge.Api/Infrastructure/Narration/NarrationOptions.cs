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
}

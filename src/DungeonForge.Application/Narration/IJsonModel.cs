namespace DungeonForge.Application.Narration;

public enum ModelFailure
{
    None,
    RateLimited,
    Timeout,
    Unavailable,
    Error,
}

public readonly record struct ModelReply(string? Text, ModelFailure Failure)
{
    public bool Succeeded => Failure == ModelFailure.None && !string.IsNullOrWhiteSpace(Text);

    public static ModelReply Fail(ModelFailure failure) => new(null, failure);
}

// A single JSON-in, JSON-out model call (temperature 0). Never throws except on caller cancellation; every failure is a
// ModelFailure so callers can fall back to deterministic C#.
public interface IJsonModel
{
    Task<ModelReply> CompleteJsonAsync(string system, string user, int maxTokens, CancellationToken ct);
}

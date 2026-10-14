using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public enum LocalNarratorState
{
    // Not probed yet.
    Unknown,
    // No WebGPU (or the module failed to load): the server narrator handles every turn.
    Unsupported,
    // Supported, but the player has not opted in to the model download.
    Idle,
    Downloading,
    Ready,
    Failed,
}

// A small language model running in this browser (WebLLM / WebGPU). It only writes flavor text from the prompts the
// server hands out; every rule and roll stays in C# on the server.
public interface ILocalNarrator
{
    LocalNarratorState State { get; }

    // 0-100 while Downloading.
    int Progress { get; }

    string? Status { get; }

    event Action? Changed;

    // Probes for WebGPU and, if the player opted in earlier, loads the (cached) model in the background.
    Task InitializeAsync();

    // The player's explicit consent to the one-time model download.
    Task EnableAsync();

    // Null when the model is not ready or produced nothing.
    Task<string?> GenerateAsync(NarrationBrief brief, CancellationToken ct = default);
}

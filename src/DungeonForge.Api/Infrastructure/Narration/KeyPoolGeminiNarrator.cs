using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

// Rotates API keys on HTTP 429; any other failure (or exhaustion) yields deterministic fallback flavor.
public sealed class KeyPoolGeminiNarrator(
    GeminiNarratorService gemini,
    IKeyPool pool,
    ILogger<KeyPoolGeminiNarrator> logger) : IDungeonNarrator
{
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(60);
    private int _exhaustionLogged;

    public async Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct)
    {
        var result = await RotateAsync((key, token) => gemini.TryGenerateChoicesAsync(ctx, key, token), ct);
        return result ?? FallbackFlavor.Choices(ctx.Room, ctx.Turn);
    }

    public async Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct)
    {
        var result = await RotateAsync((key, token) => gemini.TryNarrateOutcomeAsync(chosenOption, success, ctx, key, token), ct);
        return result ?? FallbackFlavor.Outcome(ctx.Room, success, ctx.Turn);
    }

    private async Task<T?> RotateAsync<T>(Func<string, CancellationToken, Task<GeminiResult<T>>> call, CancellationToken ct)
        where T : class
    {
        for (var attempt = 0; attempt < pool.Count; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (pool.Next() is not { } lease)
            {
                break;
            }

            Volatile.Write(ref _exhaustionLogged, 0);

            var result = await call(lease.Key, ct);
            if (!result.RateLimited)
            {
                return result.Value;
            }

            var cooldown = result.RetryAfter ?? DefaultCooldown;
            pool.Cooldown(lease.Index, cooldown);
            logger.LogWarning("Narration key #{KeyIndex} rate limited; cooling for {Seconds}s.", lease.Index, (int)cooldown.TotalSeconds);
        }

        // Warn once per exhaustion episode; repeats while keys keep cooling are debug noise.
        if (Interlocked.Exchange(ref _exhaustionLogged, 1) == 0)
        {
            logger.LogWarning("All narration keys are rate limited; using fallback until one recovers.");
        }
        else
        {
            logger.LogDebug("No narration key available; using fallback.");
        }

        return null;
    }
}

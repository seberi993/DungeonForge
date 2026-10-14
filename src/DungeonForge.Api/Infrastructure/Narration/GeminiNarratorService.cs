using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using Microsoft.Extensions.Options;

namespace DungeonForge.Api.Infrastructure.Narration;

public sealed class GeminiNarratorService(
    HttpClient http,
    IOptions<NarrationOptions> options,
    ILogger<GeminiNarratorService> logger,
    TimeProvider? clock = null) : IDungeonNarrator
{
    // Output budgets: a scenario is ~180 words; a turn call returns an outcome plus a scenario as JSON.
    private const int ScenarioMaxTokens = 700;
    private const int TurnMaxTokens = 1400;

    private readonly NarrationOptions _options = options.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct)
    {
        var result = await TryGenerateScenarioAsync(ctx, _options.ApiKey, ct);
        return result.Value ?? FallbackFlavor.Scenario(ctx.Room, ctx.Turn);
    }

    public async Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct)
    {
        var result = await TryNarrateTurnAsync(success, ctx, _options.ApiKey, ct);
        return FallbackFlavor.Complete(result.Value, success, ctx);
    }

    // Per-request key variants: Value is null on any failure; RateLimited is set on HTTP 429.
    public async Task<GeminiResult<string>> TryGenerateScenarioAsync(NarrationContext ctx, string? apiKey, CancellationToken ct)
    {
        var send = await SendAsync(NarrationPrompts.Scenario(ctx.Room), MasterPromptBuilder.BuildScenario(ctx), ScenarioMaxTokens, apiKey, ct);
        var cleaned = NarrationText.Clean(send.Text, TurnNarrationParser.MaxScenarioLength);
        return cleaned.Length == 0
            ? new(null, send.RateLimited, send.RetryAfter)
            : new(cleaned, false, null);
    }

    // One call returns both the outcome narration and the next scenario; unusable replies yield a null Value.
    public async Task<GeminiResult<TurnNarration>> TryNarrateTurnAsync(bool success, NarrationContext ctx, string? apiKey, CancellationToken ct)
    {
        var send = await SendAsync(NarrationPrompts.Turn(ctx.NextRoom), MasterPromptBuilder.Build(success, ctx), TurnMaxTokens, apiKey, ct);
        if (send.Text is null)
        {
            return new(null, send.RateLimited, send.RetryAfter);
        }

        if (TurnNarrationParser.Parse(send.Text) is { } parsed)
        {
            return new(parsed, false, null);
        }

        logger.LogWarning("Narration turn response was malformed; using fallback.");
        return new(null, false, null);
    }

    // Text is null on any failure (blank key, timeout, non-2xx, empty content); HTTP 429 sets RateLimited.
    // Only caller cancellation propagates.
    private async Task<SendResult> SendAsync(string system, string userPrompt, int maxTokens, string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return SendResult.Failed;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var body = new GenerateRequest(
                new Content(null, [new Part(system)]),
                [new Content("user", [new Part(userPrompt)])],
                new GenerationConfig(_options.Temperature, maxTokens, new ThinkingConfig(0)));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1beta/models/{_options.Model}:generateContent")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var quota = await RateLimitInfo.ReadAsync(response, timeout.Token);
                logger.LogWarning("Narration API rate limited (429); quota {QuotaId}.", quota.QuotaId ?? "unknown");
                var retry = response.Headers.RetryAfter;
                TimeSpan? wait = retry?.Delta
                    ?? (retry?.Date is { } at ? at - _clock.GetUtcNow() : (TimeSpan?)null)
                    ?? quota.RetryDelay;
                if (wait is { } w && w <= TimeSpan.Zero)
                {
                    wait = null;
                }

                if (quota.IsDaily)
                {
                    wait = wait is { } d && d > RateLimitInfo.DailyCooldown ? d : RateLimitInfo.DailyCooldown;
                }

                return new SendResult(null, true, wait);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Narration API returned {Status}.", (int)response.StatusCode);
            }

            response.EnsureSuccessStatusCode();

            var reply = await response.Content.ReadFromJsonAsync<GenerateResponse>(timeout.Token);
            var text = reply?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new SendResult(text, false, null);
            }

            logger.LogWarning("Narration response contained no text; using fallback.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Narration call failed ({Reason}); using fallback.", ex.GetType().Name);
        }

        return SendResult.Failed;
    }

    private readonly record struct SendResult(string? Text, bool RateLimited, TimeSpan? RetryAfter)
    {
        public static SendResult Failed => default;
    }

    private sealed record GenerateRequest(
        [property: JsonPropertyName("systemInstruction")] Content SystemInstruction,
        [property: JsonPropertyName("contents")] Content[] Contents,
        [property: JsonPropertyName("generationConfig")] GenerationConfig GenerationConfig);

    private sealed record Content(
        [property: JsonPropertyName("role"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role,
        [property: JsonPropertyName("parts")] List<Part>? Parts);

    private sealed record Part(
        [property: JsonPropertyName("text")] string? Text);

    // thinkingBudget 0 stops Gemini 2.5 from spending the small output budget on hidden reasoning.
    private sealed record GenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens,
        [property: JsonPropertyName("thinkingConfig")] ThinkingConfig ThinkingConfig);

    private sealed record ThinkingConfig(
        [property: JsonPropertyName("thinkingBudget")] int ThinkingBudget);

    private sealed record GenerateResponse(
        [property: JsonPropertyName("candidates")] List<Candidate>? Candidates);

    private sealed record Candidate(
        [property: JsonPropertyName("content")] Content? Content);
}

public readonly record struct GeminiResult<T>(T? Value, bool RateLimited, TimeSpan? RetryAfter);

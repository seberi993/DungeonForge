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
    private const string ChoicesSystemPrompt =
        "You write dungeon encounter dilemmas. Reply with strict JSON only, no markdown, in the shape " +
        "{\"prompt\":\"one or two atmospheric sentences\",\"options\":[\"safest\",\"balanced\",\"riskiest\"]}. " +
        "Provide exactly 3 short options (under 8 words each) ordered from safest to riskiest. " +
        "Do not include numbers, statistics, rules, or outcomes; only describe the scene and the options.";

    private const string OutcomeSystemPrompt =
        "You write dungeon outcome narration. Reply with 1 or 2 short sentences describing how the chosen action " +
        "went, matching the stated result exactly. Do not include any number, statistic, rule, or reward; " +
        "never change the stated result. When member inputs are listed, weave the ideas that fit into the narration, in the order given.";

    private const int ChoicesMaxTokens = 300;
    private const int OutcomeMaxTokens = 120;
    private const int OptionCount = 3;
    private const int MaxPromptLength = 240;
    private const int MaxOutcomeLength = 300;
    private const int MaxPartyNameLength = MasterPromptBuilder.MaxNameLength;
    private const string NeutralPartyName = "the party";
    private const int MaxOptionLength = MasterPromptBuilder.MaxOptionLength;

    private readonly NarrationOptions _options = options.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct)
    {
        var result = await TryGenerateChoicesAsync(ctx, _options.ApiKey, ct);
        return result.Value ?? FallbackFlavor.Choices(ctx.Room, ctx.Turn);
    }

    public async Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct)
    {
        var result = await TryNarrateOutcomeAsync(chosenOption, success, ctx, _options.ApiKey, ct);
        return result.Value ?? FallbackFlavor.Outcome(ctx.Room, success, ctx.Turn);
    }

    // Per-request key variants: Value is null on any failure; RateLimited is set on HTTP 429.
    public async Task<GeminiResult<EncounterChoice>> TryGenerateChoicesAsync(NarrationContext ctx, string? apiKey, CancellationToken ct)
    {
        var userPrompt = $"Party: {NarrationText.Sanitize(ctx.PartyName, MaxPartyNameLength, NeutralPartyName)}. Scene: {MasterPromptBuilder.Scene(ctx.Room)}. Mood: the party is {MasterPromptBuilder.Mood(ctx.PartyWounded)}.";

        var send = await SendAsync(ChoicesSystemPrompt, userPrompt, ChoicesMaxTokens, apiKey, ct);
        if (send.Text is null)
        {
            return new(null, send.RateLimited, send.RetryAfter);
        }

        if (TryParseChoices(send.Text) is { } parsed)
        {
            return new(parsed, false, null);
        }

        logger.LogWarning("Narration choices response was malformed; using fallback.");
        return new(null, false, null);
    }

    public async Task<GeminiResult<string>> TryNarrateOutcomeAsync(
        string chosenOption, bool success, NarrationContext ctx, string? apiKey, CancellationToken ct)
    {
        var userPrompt = MasterPromptBuilder.Build(chosenOption, success, ctx);

        var send = await SendAsync(OutcomeSystemPrompt, userPrompt, OutcomeMaxTokens, apiKey, ct);
        var cleaned = NarrationText.Clean(send.Text, MaxOutcomeLength);
        return cleaned.Length == 0
            ? new(null, send.RateLimited, send.RetryAfter)
            : new(cleaned, false, null);
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

    // Defensive: tolerates code fences / chatter around the JSON object; any wrong shape yields null.
    private static EncounterChoice? TryParseChoices(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("prompt", out var promptEl) || promptEl.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("options", out var optionsEl) || optionsEl.ValueKind != JsonValueKind.Array
                || optionsEl.GetArrayLength() != OptionCount)
            {
                return null;
            }

            var prompt = NarrationText.Clean(promptEl.GetString(), MaxPromptLength);
            if (prompt.Length == 0)
            {
                return null;
            }

            var parsed = new List<string>(OptionCount);
            foreach (var el in optionsEl.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var option = NarrationText.Clean(el.GetString(), MaxOptionLength);
                if (option.Length == 0)
                {
                    return null;
                }

                parsed.Add(option);
            }

            return new EncounterChoice(prompt, parsed);
        }
        catch (JsonException)
        {
            return null;
        }
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

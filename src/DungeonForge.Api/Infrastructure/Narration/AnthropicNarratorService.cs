using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using Microsoft.Extensions.Options;

namespace DungeonForge.Api.Infrastructure.Narration;

public sealed class AnthropicNarratorService(
    HttpClient http,
    IOptions<NarrationOptions> options,
    ILogger<AnthropicNarratorService> logger) : IDungeonNarrator
{
    private const string ChoicesSystemPrompt =
        "You write dungeon encounter dilemmas. Reply with strict JSON only, no markdown, in the shape " +
        "{\"prompt\":\"one or two atmospheric sentences\",\"options\":[\"safest\",\"balanced\",\"riskiest\"]}. " +
        "Provide exactly 3 short options (under 8 words each) ordered from safest to riskiest. " +
        "Do not include numbers, statistics, rules, or outcomes; only describe the scene and the options.";

    private const string OutcomeSystemPrompt =
        "You write dungeon outcome narration. Reply with 1 or 2 short sentences describing how the chosen action " +
        "went, matching the stated result exactly. Do not include any number, statistic, rule, or reward; " +
        "never change the stated result.";

    private const int ChoicesMaxTokens = 300;
    private const int OutcomeMaxTokens = 120;
    private const int OptionCount = 3;
    private const int MaxPromptLength = 240;
    private const int MaxOptionLength = 80;
    private const int MaxOutcomeLength = 300;
    private const int MaxPartyNameLength = 40;
    private const string NeutralPartyName = "the party";
    private const string NeutralOption = "the chosen action";

    private readonly NarrationOptions _options = options.Value;

    public async Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct)
    {
        var userPrompt = $"Party: {Sanitize(ctx.PartyName, MaxPartyNameLength, NeutralPartyName)}. Scene: {Scene(ctx)}. Mood: the party is {Mood(ctx)}.";

        var text = await SendAsync(ChoicesSystemPrompt, userPrompt, ChoicesMaxTokens, ct);
        if (text is not null)
        {
            if (TryParseChoices(text) is { } parsed)
            {
                return parsed;
            }

            logger.LogWarning("Narration choices response was malformed; using fallback.");
        }

        return FallbackFlavor.Choices(ctx.Room, ctx.Turn);
    }

    public async Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct)
    {
        var result = success ? "succeeded" : "failed";
        var userPrompt =
            $"Party: {Sanitize(ctx.PartyName, MaxPartyNameLength, NeutralPartyName)}. Scene: {Scene(ctx)}. " +
            $"Mood: the party is {Mood(ctx)}. Chosen action: {Sanitize(chosenOption, MaxOptionLength, NeutralOption)}. " +
            $"Result: the action {result}.";

        var text = await SendAsync(OutcomeSystemPrompt, userPrompt, OutcomeMaxTokens, ct);
        var cleaned = Clean(text, MaxOutcomeLength);
        return cleaned.Length == 0 ? FallbackFlavor.Outcome(ctx.Room, success, ctx.Turn) : cleaned;
    }

    private static string Scene(NarrationContext ctx) => ctx.Room == RoomType.Combat ? "a combat encounter" : "a treasure chest";

    private static string Mood(NarrationContext ctx) => ctx.PartyWounded ? "battered and weary" : "healthy and confident";

    // Returns trimmed model text, or null on any failure (blank key, timeout, non-2xx, empty content).
    // Only caller cancellation propagates.
    private async Task<string?> SendAsync(string system, string userPrompt, int maxTokens, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var body = new MessageRequest(_options.Model, maxTokens, _options.Temperature, system, [new RequestMessage("user", userPrompt)]);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Add("x-api-key", _options.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();

            var message = await response.Content.ReadFromJsonAsync<MessageResponse>(timeout.Token);
            var text = message?.Content?.FirstOrDefault()?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
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

        return null;
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

            var prompt = Clean(promptEl.GetString(), MaxPromptLength);
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

                var option = Clean(el.GetString(), MaxOptionLength);
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

    private static string Clean(string? value, int maxLength)
    {
        var cleaned = Collapse(value);
        return cleaned.Length > maxLength ? cleaned[..maxLength].TrimEnd() : cleaned;
    }

    private static string Sanitize(string? value, int maxLength, string neutral)
    {
        var cleaned = Clean(value, maxLength);
        return cleaned.Length == 0 ? neutral : cleaned;
    }

    // Collapses whitespace/control characters into single spaces so untrusted text stays on one line.
    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    private sealed record MessageRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] RequestMessage[] Messages);

    private sealed record RequestMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record MessageResponse(
        [property: JsonPropertyName("content")] List<ContentBlock>? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("text")] string? Text);
}

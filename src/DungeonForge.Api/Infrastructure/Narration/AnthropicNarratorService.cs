using System.Net.Http.Json;
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
    private const string SystemPrompt =
        "You write atmospheric dungeon flavor text. Reply with exactly 2 short sentences. " +
        "Do not include numbers, statistics, rules, or outcomes; only describe the scene.";

    private const int MaxTokens = 80;

    private readonly NarrationOptions _options = options.Value;

    public async Task<string> NarrateAsync(NarrationContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return FallbackFlavor.Pick(ctx.Room, ctx.Turn);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = JsonContent.Create(BuildRequest(ctx)),
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

        return FallbackFlavor.Pick(ctx.Room, ctx.Turn);
    }

    private MessageRequest BuildRequest(NarrationContext ctx)
    {
        var room = ctx.Room == RoomType.Combat ? "a combat encounter" : "a treasure chest";
        var mood = ctx.PartyWounded ? "battered and weary" : "healthy and confident";
        var prompt = $"Party: {SanitizePartyName(ctx.PartyName)}. Scene: {room}. Mood: the party is {mood}.";
        return new MessageRequest(_options.Model, MaxTokens, SystemPrompt, [new RequestMessage("user", prompt)]);
    }

    private const int MaxPartyNameLength = 40;
    private const string NeutralPartyName = "the party";

    private static string SanitizePartyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NeutralPartyName;
        }

        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
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

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length > MaxPartyNameLength)
        {
            cleaned = cleaned[..MaxPartyNameLength].TrimEnd();
        }

        return cleaned.Length == 0 ? NeutralPartyName : cleaned;
    }

    private sealed record MessageRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
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

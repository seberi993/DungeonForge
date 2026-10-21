using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Narration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DungeonForge.Infrastructure.Narration;

// Gemini over the shared key pool: a 429 cools the key down and the next one is tried.
public sealed class GeminiJsonModel(HttpClient http, IKeyPool pool, IOptions<NarrationOptions> options) : IJsonModel
{
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(60);

    public async Task<ModelReply> CompleteJsonAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled || pool.Count == 0)
        {
            return ModelReply.Fail(ModelFailure.Unavailable);
        }

        var limited = false;
        for (var attempt = 0; attempt < pool.Count; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (pool.Next() is not { } lease)
            {
                break;
            }

            var (reply, retry) = await SendAsync(o, lease.Key, system, user, maxTokens, ct);
            if (reply.Failure != ModelFailure.RateLimited)
            {
                return reply;
            }

            limited = true;
            pool.Cooldown(lease.Index, retry ?? DefaultCooldown);
        }

        return ModelReply.Fail(limited ? ModelFailure.RateLimited : ModelFailure.Unavailable);
    }

    private async Task<(ModelReply Reply, TimeSpan? Retry)> SendAsync(
        NarrationOptions o, string key, string system, string user, int maxTokens, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
        try
        {
            var body = new
            {
                systemInstruction = new { parts = new[] { new { text = system } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
                generationConfig = new
                {
                    temperature = 0,
                    maxOutputTokens = maxTokens,
                    responseMimeType = "application/json",
                    thinkingConfig = new { thinkingLevel = "minimal" },
                },
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1beta/models/{o.Model}:generateContent")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Add("x-goog-api-key", key);

            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var quota = await RateLimitInfo.ReadAsync(response, timeout.Token);
                var wait = response.Headers.RetryAfter?.Delta ?? quota.RetryDelay;
                if (quota.IsDaily)
                {
                    wait = wait is { } d && d > RateLimitInfo.DailyCooldown ? d : RateLimitInfo.DailyCooldown;
                }

                return (ModelReply.Fail(ModelFailure.RateLimited), wait);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (ModelReply.Fail(ModelFailure.Error), null);
            }

            var reply = await response.Content.ReadFromJsonAsync<Envelope>(timeout.Token);
            var text = reply?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text?.Trim();
            return string.IsNullOrWhiteSpace(text)
                ? (ModelReply.Fail(ModelFailure.Error), null)
                : (new ModelReply(text, ModelFailure.None), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return (ModelReply.Fail(ModelFailure.Timeout), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            return (ModelReply.Fail(ModelFailure.Error), null);
        }
    }

    private sealed record Envelope([property: JsonPropertyName("candidates")] List<Candidate>? Candidates);

    private sealed record Candidate([property: JsonPropertyName("content")] Body? Content);

    private sealed record Body([property: JsonPropertyName("parts")] List<TextPart>? Parts);

    private sealed record TextPart([property: JsonPropertyName("text")] string? Text);
}

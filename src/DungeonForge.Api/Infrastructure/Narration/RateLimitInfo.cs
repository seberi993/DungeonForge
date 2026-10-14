using System.Globalization;
using System.Text.Json;

namespace DungeonForge.Api.Infrastructure.Narration;

// Parsed from Google's 429 body: RetryInfo.retryDelay ("34s") and QuotaFailure.violations[].quotaId.
internal readonly record struct RateLimitInfo(TimeSpan? RetryDelay, string? QuotaId)
{
    public static readonly TimeSpan DailyCooldown = TimeSpan.FromHours(1);

    public bool IsDaily => QuotaId?.Contains("PerDay", StringComparison.OrdinalIgnoreCase) == true;

    public static async Task<RateLimitInfo> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            return default;
        }
    }

    public static RateLimitInfo Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var error)
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return default;
            }

            TimeSpan? delay = null;
            string? quotaId = null;
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("retryDelay", out var rd) && rd.ValueKind == JsonValueKind.String
                    && rd.GetString() is { } text && text.EndsWith('s')
                    && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    && seconds > 0 && seconds < TimeSpan.FromDays(1).TotalSeconds)
                {
                    delay = TimeSpan.FromSeconds(seconds);
                }

                if (quotaId is null && detail.TryGetProperty("violations", out var violations)
                    && violations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in violations.EnumerateArray())
                    {
                        if (v.TryGetProperty("quotaId", out var q) && q.ValueKind == JsonValueKind.String)
                        {
                            quotaId = q.GetString();
                            break;
                        }
                    }
                }
            }

            return new RateLimitInfo(delay, quotaId);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

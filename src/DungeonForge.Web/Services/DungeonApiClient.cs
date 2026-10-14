using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public sealed class DungeonApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public Task<ApiResult<PartyResponse>> CreatePartyAsync(string name, IReadOnlyList<MemberRequest> members, CancellationToken ct = default) =>
        SendAsync<PartyResponse>(() => http.PostAsJsonAsync("/api/parties", new CreatePartyRequest(name, members), Json, ct), ct);

    public Task<ApiResult<PartyStateDto>> GetPartyAsync(Guid partyId, CancellationToken ct = default) =>
        SendAsync<PartyStateDto>(() => http.GetAsync($"/api/parties/{partyId}", ct), ct);

    public Task<ApiResult<PartyStateDto>> GetPartyByCodeAsync(string inviteCode, CancellationToken ct = default) =>
        SendAsync<PartyStateDto>(() => http.GetAsync($"/api/parties/by-code/{Uri.EscapeDataString(inviteCode.Trim())}", ct), ct);

    // No index and no actions = request (or re-read) the pending prompt, sent without a body.
    // Actions only = submit tactical input; an index resolves the chosen option (actions are submitted first).
    public Task<ApiResult<ExploreResponse>> ExploreAsync(Guid partyId, int? choiceIndex = null, IReadOnlyList<PlayerAction>? actions = null, CancellationToken ct = default) =>
        SendAsync<ExploreResponse>(() => choiceIndex is null && actions is not { Count: > 0 }
            ? http.PostAsync($"/api/parties/{partyId}/explore", null, ct)
            : http.PostAsJsonAsync($"/api/parties/{partyId}/explore", new ExploreRequest(choiceIndex, actions is { Count: > 0 } ? actions : null), Json, ct), ct);

    private static async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await send();
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return new(ApiStatus.NotFound, Error: "Not found.");
                case HttpStatusCode.Conflict:
                    return new(ApiStatus.Conflict, Error: "Conflict.");
                case HttpStatusCode.BadRequest:
                    return new(ApiStatus.ValidationError, Error: await ReadValidationMessageAsync(response, ct));
            }

            if (!response.IsSuccessStatusCode)
                return new(ApiStatus.Failed, Error: $"Server returned {(int)response.StatusCode}.");

            var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return value is null
                ? new(ApiStatus.Failed, Error: "Empty response from server.")
                : new(ApiStatus.Success, value);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
        {
            return new(ApiStatus.Failed, Error: "Could not reach the server.");
        }
    }

    private static async Task<string> ReadValidationMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const string fallback = "The request was invalid.";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray() : [])
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
                if (messages.Count > 0) return string.Join(" ", messages);
            }
        }
        catch (JsonException)
        {
        }

        return fallback;
    }
}

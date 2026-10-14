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

    public Task<ApiResult<JoinPartyResponse>> JoinPartyAsync(string inviteCode, string name, CharacterClass characterClass, CancellationToken ct = default) =>
        SendAsync<JoinPartyResponse>(() => http.PostAsJsonAsync($"/api/parties/by-code/{Uri.EscapeDataString(inviteCode.Trim())}/join", new JoinPartyRequest(name, characterClass), Json, ct), ct);

    // No actions = request (or re-read) the pending scenario, sent without a body.
    // Actions + submitOnly = share input with the party; actions alone = resolve the turn.
    public Task<ApiResult<ExploreResponse>> ExploreAsync(Guid partyId, IReadOnlyList<PlayerAction>? actions = null, bool submitOnly = false, CancellationToken ct = default) =>
        SendAsync<ExploreResponse>(() => actions is not { Count: > 0 }
            ? http.PostAsync($"/api/parties/{partyId}/explore", null, ct)
            : http.PostAsJsonAsync($"/api/parties/{partyId}/explore", new ExploreRequest(actions, submitOnly ? true : null), Json, ct), ct);

    public Task<ApiResult<IReadOnlyList<SpellDto>>> GetSpellsAsync(CharacterClass characterClass, CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<SpellDto>>(() => http.GetAsync($"/api/classes/{characterClass.ToString().ToLowerInvariant()}/spells", ct), ct);

    public Task<ApiResult<HighScoresResponse>> GetHighScoresAsync(int limit = 10, CancellationToken ct = default) =>
        SendAsync<HighScoresResponse>(() => http.GetAsync($"/api/scores?limit={limit}", ct), ct);

    public Task<ApiResult<UnlockResponse>> UnlockAsync(Guid partyId, Guid memberId, CancellationToken ct = default) =>
        SendAsync<UnlockResponse>(() => http.PostAsJsonAsync($"/api/parties/{partyId}/unlock", new UnlockRequest(memberId), Json, ct), ct);

    // Hands the local model's text (or a decline) for the briefed turn back to the server. False when it was not accepted.
    public async Task<bool> SubmitNarrationAsync(Guid partyId, int turn, string? text, bool declined, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                $"/api/parties/{partyId}/narration", new SubmitNarrationRequest(turn, text, declined ? true : null), Json, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

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
                    return await IsWaitingBodyAsync(response, ct)
                        ? new(ApiStatus.Waiting, Error: "Waiting for party members.")
                        : new(ApiStatus.Conflict, Error: "Conflict.");
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

    // A 409 with {"status":"waiting"} means other members have not locked in yet; any other 409 means the party is defeated.
    private static async Task<bool> IsWaitingBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("status", out var status) &&
                   status.ValueKind == JsonValueKind.String &&
                   status.GetString() == "waiting";
        }
        catch (JsonException)
        {
            return false;
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

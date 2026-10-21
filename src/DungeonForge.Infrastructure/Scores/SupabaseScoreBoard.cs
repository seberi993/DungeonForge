using DungeonForge.Domain;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DungeonForge.Application.Features.Scores;

namespace DungeonForge.Infrastructure.Scores;

// Supabase PostgREST over plain HTTP (no extra packages). Table: see schema.sql. One row per finished run, written once.
public sealed class SupabaseScoreBoard(HttpClient http) : IScoreBoard
{
    private const string Table = "rest/v1/run_scores";

    private sealed record Row(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("party_name")] string PartyName,
        [property: JsonPropertyName("heroes")] string[] Heroes,
        [property: JsonPropertyName("floor")] int Floor,
        [property: JsonPropertyName("turns")] int Turns,
        [property: JsonPropertyName("gold")] int Gold,
        [property: JsonPropertyName("achieved_at")] DateTimeOffset AchievedAt);

    public async Task RecordAsync(RunScore score, CancellationToken ct = default)
    {
        var row = new Row(score.Id, score.PartyName, [.. score.Heroes], score.Floor, score.Turns, score.Gold, score.AchievedAt);
        using var request = new HttpRequestMessage(HttpMethod.Post, Table) { Content = JsonContent.Create(row) };
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<RunScore>> TopAsync(int limit, CancellationToken ct = default)
    {
        var rows = await http.GetFromJsonAsync<Row[]>(
            $"{Table}?select=*&order=floor.desc,gold.desc,turns.desc,achieved_at.asc&limit={limit}", ct) ?? [];
        return [.. rows.Select(r => new RunScore(r.Id, r.PartyName, r.Heroes, r.Floor, r.Turns, r.Gold, r.AchievedAt))];
    }
}

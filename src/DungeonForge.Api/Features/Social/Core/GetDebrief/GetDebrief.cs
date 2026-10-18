using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Social.Core.GetDebrief;

public sealed record DebriefAbility(string Name, string Archetype, string Rarity, int Budget, int Cooldown, IReadOnlyDictionary<string, int> Effects);

public sealed record DebriefMoment(int Turn, string Type, string Verdict, string Rule, string Rationale, bool Model);

// Exact values, shown only after the run and only to a player who opted in. Everyone gets the run's counters.
public sealed record DebriefResponse(
    string PlayerName,
    bool OptedIn,
    IReadOnlyDictionary<string, int>? Axes,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Tone,
    int? Trust,
    int? Menace,
    int? Credibility,
    IReadOnlyList<DebriefAbility>? Abilities,
    IReadOnlyList<DebriefMoment>? Moments,
    IReadOnlyDictionary<string, int> Run);

// The one place raw numbers leave the server. Until the run is over the dungeon keeps its secrets (409).
public sealed class GetDebriefHandler(SocialAccess access)
{
    public const string Operation = "Query:GetDebrief";

    public Result<DebriefResponse> Handle(Guid id, string? playerName)
    {
        var resolved = access.Resolve(id, playerName, module: null, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        lock (party.SyncRoot)
        {
            if (!party.IsPartyDefeated)
            {
                return SocialErrors.RunInProgress(Operation, id);
            }

            var social = party.Social();
            var profile = social.Profile(member.Name);
            var run = new Dictionary<string, int>(social.Counters);
            if (!profile.DebriefOptIn)
            {
                return new DebriefResponse(member.Name, false, null, profile.Tags, profile.Tone, null, null, null, null, null, run);
            }

            var axes = profile.Axes.ToDictionary().ToDictionary(a => Camel(a.Key.ToString()), a => a.Value);
            var abilities = profile.Abilities.Select(a => new DebriefAbility(
                a.Name, a.Archetype, a.Rarity, a.Stats.Budget, a.Stats.Cooldown,
                a.Stats.Effects.ToDictionary(e => e.Type, e => e.Amount))).ToArray();
            var moments = social.Journal
                .Where(e => e.Decision.Verdict != Verdict.Ignore && Involves(e.Event, member.Name))
                .Select(e => new DebriefMoment(
                    e.Event.Turn, e.Event.Type, e.Decision.Verdict.ToString().ToLowerInvariant(), e.Decision.Rule, e.Decision.Rationale, e.Decision.Llm))
                .ToArray();

            return new DebriefResponse(
                member.Name, true, axes, profile.Tags, profile.Tone, profile.Trust, profile.Menace, profile.Credibility, abilities, moments, run);
        }
    }

    private static bool Involves(SocialEvent e, string name) =>
        e.Actors.Contains(name, StringComparer.OrdinalIgnoreCase)
        || (e.Data.TryGetValue("from", out var from) && string.Equals(from, name, StringComparison.OrdinalIgnoreCase));

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}

public sealed class GetDebriefEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}/social/debrief", IResult (Guid id, string? playerName, GetDebriefHandler handler, HttpContext http) =>
                handler.Handle(id, playerName).ToHttp(http))
           .WithName("GetDebrief");
}

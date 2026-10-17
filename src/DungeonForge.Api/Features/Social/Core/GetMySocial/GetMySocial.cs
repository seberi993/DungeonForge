using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Social.Abilities;
using DungeonForge.Api.Features.Social.Asks;
using DungeonForge.Api.Features.Social.Asks.SendAsk;
using DungeonForge.Api.Features.Social.Seed;

namespace DungeonForge.Api.Features.Social.Core.GetMySocial;

// Opaque by design: flavor, tags and a few qualitative words. No axis values, no budgets, no cooldown numbers.
public sealed record AbilityDto(string Id, string Name, string Flavor, string Hint, bool Ready);

public sealed record InboxAskDto(string AskId, string From, AskKind Kind, string Text, DateTimeOffset ExpiresAt, bool Replied);

public sealed record SentReplyDto(string By, string Summary);

public sealed record SentAskDto(
    string AskId, IReadOnlyList<string> To, AskKind Kind, string Text, DateTimeOffset ExpiresAt, IReadOnlyList<SentReplyDto> Replies);

public sealed record MySocialResponse(
    string PlayerName,
    string? SeedQuestion,
    bool Seeded,
    bool Finalized,
    IReadOnlyList<string> Tags,
    string Standing,
    IReadOnlyList<AbilityDto> Abilities,
    int AskTokens,
    bool AsksAvailable,
    IReadOnlyList<InboxAskDto> Inbox,
    IReadOnlyList<SentAskDto> Sent);

public sealed class GetMySocialHandler(SocialAccess access, SocialModules modules, TimeProvider time)
{
    public const string Operation = "Query:GetMySocial";

    public Result<MySocialResponse> Handle(Guid id, string? playerName)
    {
        var resolved = access.Resolve(id, playerName, module: null, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        var now = time.GetUtcNow();
        lock (party.SyncRoot)
        {
            var social = party.Social();
            var profile = social.Profile(member.Name);
            SendAskHandler.Purge(social, now);

            var asksOn = modules.IsEnabled(SocialModule.Asks);
            var abilities = modules.IsEnabled(SocialModule.Abilities)
                ? profile.Abilities.Select(a => new AbilityDto(a.Id, a.Name, a.Flavor, a.Hint, AbilityUse.IsReady(profile, a, party.Turn))).ToArray()
                : [];
            var live = social.Asks.Where(a => !a.IsExpired(now) && a.Involves(member.Name)).ToArray();

            var inbox = asksOn
                ? live.Where(a => a.To.Contains(member.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(a => new InboxAskDto(a.Id, a.From, a.Kind, a.Text, a.ExpiresAt, a.Replies.ContainsKey(member.Name))).ToArray()
                : [];
            var sent = asksOn
                ? live.Where(a => string.Equals(a.From, member.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(a => new SentAskDto(a.Id, a.To, a.Kind, a.Text, a.ExpiresAt,
                        [.. a.Replies.Values.Select(r => new SentReplyDto(r.By, Summary(r)))])).ToArray()
                : [];

            return new MySocialResponse(
                member.Name,
                modules.IsEnabled(SocialModule.Seed) && !profile.Seeded ? SeedQuestions.For(social.RunSeed) : null,
                profile.Seeded,
                social.Finalized,
                profile.Tags,
                Standing(profile),
                abilities,
                profile.AskTokens,
                asksOn && party.Members.Count >= modules.MinPlayersForAsks,
                inbox,
                sent);
        }
    }

    // A bluff reads exactly like an honest answer; only a dodge shows.
    private static string Summary(AskReply r) =>
        r.Choice == AskChoice.Dodge ? "slips the question" : r.Text ?? "answers plainly";

    private static string Standing(PlayerProfile p) => p.Credibility switch { 0 => "doubted", >= 3 => "trusted", _ => "steady" };
}

public sealed class GetMySocialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}/social/me", IResult (Guid id, string? playerName, GetMySocialHandler handler, HttpContext http) =>
                handler.Handle(id, playerName).ToHttp(http))
           .WithName("GetMySocial");
}

using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Social.Asks.SendAsk;

public sealed record SendAskRequest(string? From, IReadOnlyList<string>? To, string? Text, AskKind? Kind);

public sealed record SendAskResponse(string AskId, DateTimeOffset ExpiresAt, int TokensLeft);

// A private whisper to one to three teammates. Costs shield-tokens, is rate limited and expires on its own. Needs a party
// of more than three: with three, a private word would just be the third player being left out.
public sealed class SendAskHandler(SocialAccess access, SocialModules modules, ChaosDispatcher chaos, TimeProvider time)
{
    public const string Operation = "Command:SendAsk";

    public async Task<Result<SendAskResponse>> HandleAsync(Guid id, SendAskRequest request)
    {
        var resolved = access.Resolve(id, request.From, SocialModule.Asks, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, sender) = resolved.Value;
        var text = NarrationText.Clean(request.Text, AskThread.MaxTextLength);
        var kind = request.Kind!.Value;
        var now = time.GetUtcNow();

        AskThread thread;
        int tokensLeft;
        lock (party.SyncRoot)
        {
            if (party.Members.Count < modules.MinPlayersForAsks)
            {
                return SocialErrors.AsksUnavailable(Operation, id);
            }

            var social = party.Social();
            var recipients = (request.To ?? [])
                .Select(n => party.Members.FirstOrDefault(m => string.Equals(m.Name, n.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Where(m => m is not null && !string.Equals(m.Name, sender.Name, StringComparison.OrdinalIgnoreCase)
                    && !m.IsDefeated && social.FindProfile(m.Name) is not { OptedOut: true })
                .Select(m => m!.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (recipients.Length is 0 or > AskThread.MaxRecipients || recipients.Length != (request.To ?? []).Count)
            {
                return SocialErrors.InvalidRecipients(Operation, id);
            }

            Purge(social, now);
            var profile = social.Profile(sender.Name);
            if (profile.OptedOut)
            {
                return SocialErrors.ModuleDisabled(Operation, id);
            }

            var open = social.Asks.Count(a => string.Equals(a.From, sender.Name, StringComparison.OrdinalIgnoreCase)
                && !a.IsExpired(now) && a.Replies.Count < a.To.Count);
            if (open >= AskThread.MaxOpenPerPlayer)
            {
                return SocialErrors.TooManyOpenAsks(Operation, id, sender.Name);
            }

            if ((social.SenderCooldowns.TryGetValue(sender.Name, out var until) && now < until)
                || recipients.Any(r => social.PairCooldowns.TryGetValue((sender.Name, r), out var pair) && now < pair))
            {
                return SocialErrors.AskCooldown(Operation, id, sender.Name);
            }

            // Accusing while your word is worthless costs double.
            var cost = recipients.Length * (kind == AskKind.Accusation && profile.Credibility == 0 ? 2 : 1);
            if (profile.AskTokens < cost)
            {
                return SocialErrors.NotEnoughTokens(Operation, id, sender.Name);
            }

            profile.AskTokens -= cost;
            tokensLeft = profile.AskTokens;
            social.SenderCooldowns[sender.Name] = now + AskThread.SenderCooldown;
            foreach (var r in recipients)
            {
                social.PairCooldowns[(sender.Name, r)] = now + AskThread.PairCooldown;
            }

            thread = new AskThread(social.NextId("ask"), sender.Name, recipients, text, kind, now, now + AskThread.Lifetime);
            social.Asks.Add(thread);
        }

        access.Touch(party);
        foreach (var recipient in thread.To)
        {
            await chaos.SendAsync(id, recipient, new PrivateMessage(PrivateKinds.Ask, $"{thread.From} whispers: {thread.Text}"));
        }

        return new SendAskResponse(thread.Id, thread.ExpiresAt, tokensLeft);
    }

    // Expired threads lose their text and replies; nothing but the audit journal remembers them.
    public static void Purge(PartySocial social, DateTimeOffset now)
    {
        foreach (var thread in social.Asks.Where(a => !a.Purged && a.IsExpired(now)))
        {
            thread.Purge();
        }
    }
}

public sealed class SendAskEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/social/asks", async Task<IResult> (
                Guid id, SendAskRequest request, SendAskHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<SendAskRequest>(1);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.From) || request.From.Trim().Length > MaxNameLength)
               {
                   errors["from"] = [$"Sender is required and at most {MaxNameLength} characters."];
               }

               if (request?.To is not { Count: > 0 and <= AskThread.MaxRecipients })
               {
                   errors["to"] = [$"Whisper to one to {AskThread.MaxRecipients} members."];
               }

               var length = request?.Text?.Trim().Length ?? 0;
               if (length == 0 || length > AskThread.MaxTextLength)
               {
                   errors["text"] = [$"Text is required and at most {AskThread.MaxTextLength} characters."];
               }

               if (request?.Kind is not { } kind || !Enum.IsDefined(kind))
               {
                   errors["kind"] = ["Kind must be question, accusation, offer or plea."];
               }

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(SendAskHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("SendAsk");
}

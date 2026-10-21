using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Application.Narration;

namespace DungeonForge.Application.Features.Social.Asks.SendAsk;

// Compel: spend a point of credibility to press the recipients; they can only answer honestly or dodge.
public sealed record SendAskRequest(string? From, IReadOnlyList<string>? To, string? Text, AskKind? Kind, bool? Compel = null);

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
                social.Count(SocialMetrics.RateLimited);
                return SocialErrors.AskCooldown(Operation, id, sender.Name);
            }

            var compel = request.Compel == true;
            if (compel && profile.Credibility < 1)
            {
                return SocialErrors.NotEnoughCredibility(Operation, id, sender.Name);
            }

            if (social.Asks.Any(a => string.Equals(a.From, sender.Name, StringComparison.OrdinalIgnoreCase)
                && !a.IsExpired(now) && now - a.CreatedAt < AskThread.DuplicateWindow && AskText.Similar(a.Text, text)))
            {
                social.Count(SocialMetrics.AbuseFlags);
                return SocialErrors.DuplicateAsk(Operation, id, sender.Name);
            }

            // Accusing while your word is worthless costs double.
            var cost = recipients.Length * (kind == AskKind.Accusation && profile.Credibility == 0 ? 2 : 1);
            if (profile.AskTokens < cost)
            {
                return SocialErrors.NotEnoughTokens(Operation, id, sender.Name);
            }

            profile.AskTokens -= cost;
            if (compel) profile.Credibility -= 1;
            tokensLeft = profile.AskTokens;
            social.SenderCooldowns[sender.Name] = now + AskThread.SenderCooldown;
            foreach (var r in recipients)
            {
                social.PairCooldowns[(sender.Name, r)] = now + AskThread.PairCooldown;
            }

            thread = new AskThread(social.NextId("ask"), sender.Name, recipients, text, kind, now, now + AskThread.Lifetime) { Compelled = compel };
            social.Asks.Add(thread);
            social.Count(SocialMetrics.AsksSent);
            if (compel) social.Count(SocialMetrics.AsksCompelled);
        }

        access.Touch(party);
        foreach (var recipient in thread.To)
        {
            var pressed = thread.Compelled ? " Someone pressed you: answer honestly or dodge." : string.Empty;
            await chaos.SendAsync(id, recipient, new PrivateMessage(PrivateKinds.Ask, $"{thread.From} whispers: {thread.Text}{pressed}"));
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

using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Asks.SendAsk;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Application.Features.Social.Supervision;

namespace DungeonForge.Application.Features.Social.Asks.RevealAsk;

public sealed record RevealAskRequest(string? From);

public sealed record RevealAskResponse(bool Accepted = true);

// The asker spends a point of credibility to lay a whisper and the answers it drew bare to the whole party. The narrator
// treats the revealed exchange as evidence. An answer shows as its words or as a dodge, never as an honest/bluff label.
public sealed class RevealAskHandler(SocialAccess access, SocialPipeline pipeline, SocialDelivery delivery, TimeProvider time)
{
    public const string Operation = "Command:RevealAsk";

    public async Task<Result<RevealAskResponse>> HandleAsync(Guid id, string askId, RevealAskRequest request)
    {
        var resolved = access.Resolve(id, request.From, SocialModule.Asks, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        lock (party.SyncRoot)
        {
            var social = party.Social();
            SendAskHandler.Purge(social, time.GetUtcNow());
            var thread = social.Asks.FirstOrDefault(a => a.Id == askId && !a.Purged);
            if (thread is null)
            {
                return SocialErrors.AskNotFound(Operation, id, askId);
            }

            if (!string.Equals(thread.From, member.Name, StringComparison.OrdinalIgnoreCase))
            {
                return SocialErrors.NotAsker(Operation, id, member.Name);
            }

            if (thread.Revealed || thread.Replies.Count == 0)
            {
                return SocialErrors.NothingToReveal(Operation, id, askId);
            }

            var profile = social.Profile(member.Name);
            if (profile.Credibility < 1)
            {
                return SocialErrors.NotEnoughCredibility(Operation, id, member.Name);
            }

            profile.Credibility -= 1;
            thread.Revealed = true;

            var answers = string.Join(" ", thread.Replies.Values.Select(r =>
                r.Choice == AskChoice.Dodge ? $"{r.By} slips the question." : $"{r.By}: {r.Text ?? "answers plainly"}."));
            social.Outbox.Add(new SocialNotice(null, "narrator", $"{thread.From} lays a whisper bare: \"{thread.Text}\" {answers}"));

            pipeline.Process(party, pipeline.NewEvent(
                party, SocialEventTypes.AskRevealed, party.Turn, [thread.From],
                new Dictionary<string, string> { ["askId"] = thread.Id, ["kind"] = thread.Kind.ToString() }, thread.Id));
        }

        access.Touch(party);
        await delivery.DeliverAsync(party);
        return new RevealAskResponse();
    }
}

using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Asks.SendAsk;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Application.Features.Social.Supervision;
using DungeonForge.Application.Narration;

namespace DungeonForge.Application.Features.Social.Asks.ReplyAsk;

public sealed record ReplyAskRequest(string? From, AskChoice? Choice, string? Text);

public sealed record ReplyAskResponse(bool Accepted = true);

// A recipient answers honestly, dodges or bluffs. The asker sees the words (or the dodge), never the label: a bluff reads
// exactly like an honest answer. The narrator sees the label and judges it against what the dungeon knows.
public sealed class ReplyAskHandler(SocialAccess access, SocialPipeline pipeline, SocialDelivery delivery, TimeProvider time)
{
    public const string Operation = "Command:ReplyAsk";

    public async Task<Result<ReplyAskResponse>> HandleAsync(Guid id, string askId, ReplyAskRequest request)
    {
        var resolved = access.Resolve(id, request.From, SocialModule.Asks, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        var choice = request.Choice!.Value;
        var now = time.GetUtcNow();
        var reply = NarrationText.Clean(request.Text, AskThread.MaxReplyLength);

        lock (party.SyncRoot)
        {
            var social = party.Social();
            SendAskHandler.Purge(social, now);
            var thread = social.Asks.FirstOrDefault(a => a.Id == askId && !a.Purged);
            if (thread is null)
            {
                return SocialErrors.AskNotFound(Operation, id, askId);
            }

            if (!thread.To.Contains(member.Name, StringComparer.OrdinalIgnoreCase))
            {
                return SocialErrors.NotRecipient(Operation, id, member.Name);
            }

            if (thread.Replies.ContainsKey(member.Name))
            {
                return SocialErrors.AlreadyReplied(Operation, id, member.Name);
            }

            if (thread.Compelled && choice == AskChoice.Bluff)
            {
                return SocialErrors.BluffForbidden(Operation, id, member.Name);
            }

            thread.Replies[member.Name] = new AskReply(member.Name, choice, reply.Length == 0 ? null : reply, now);
            social.Count(SocialMetrics.AsksReplied);

            var seen = choice == AskChoice.Dodge
                ? $"{member.Name} slips the question."
                : reply.Length == 0 ? $"{member.Name} answers plainly." : $"{member.Name}: {reply}";
            social.Outbox.Add(new SocialNotice(thread.From, "ask", seen));

            var data = new Dictionary<string, string>
            {
                ["from"] = thread.From,
                ["kind"] = thread.Kind.ToString(),
                ["choice"] = choice.ToString(),
                ["text"] = thread.Text,
                ["askId"] = thread.Id,
            };
            pipeline.Process(party, pipeline.NewEvent(party, SocialEventTypes.AskReply, party.Turn, [member.Name], data, thread.Id));

            var done = thread.Replies.Count == thread.To.Count;
            if (done && thread.To.Count >= 2
                && (thread.Replies.Values.All(r => r.Choice == AskChoice.Honest) || thread.Replies.Values.All(r => r.Choice == AskChoice.Dodge)))
            {
                pipeline.Process(party, pipeline.NewEvent(
                    party, SocialEventTypes.AskConsensus, party.Turn, thread.To, new Dictionary<string, string> { ["from"] = thread.From }, thread.Id));
            }
        }

        access.Touch(party);
        await delivery.DeliverAsync(party);
        return new ReplyAskResponse();
    }
}

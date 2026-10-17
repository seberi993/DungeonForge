using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Social.Asks.SendAsk;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Features.Social.Supervision;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Social.Asks.ReplyAsk;

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

            thread.Replies[member.Name] = new AskReply(member.Name, choice, reply.Length == 0 ? null : reply, now);

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

public sealed class ReplyAskEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/social/asks/{askId}/reply", async Task<IResult> (
                Guid id, string askId, ReplyAskRequest request, ReplyAskHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, askId, request)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<ReplyAskRequest>(2);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.From) || request.From.Trim().Length > MaxNameLength)
               {
                   errors["from"] = [$"Responder is required and at most {MaxNameLength} characters."];
               }

               if (request?.Choice is not { } choice || !Enum.IsDefined(choice))
               {
                   errors["choice"] = ["Choice must be honest, dodge or bluff."];
               }

               if (request?.Text is { } text && text.Trim().Length > AskThread.MaxReplyLength)
               {
                   errors["text"] = [$"Text must be at most {AskThread.MaxReplyLength} characters."];
               }

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(ReplyAskHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("ReplyAsk");
}

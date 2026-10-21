using DungeonForge.Application.Features.Social.Asks;
using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Asks.ReplyAsk;

namespace DungeonForge.Api.Features.Social.Asks.ReplyAsk;

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

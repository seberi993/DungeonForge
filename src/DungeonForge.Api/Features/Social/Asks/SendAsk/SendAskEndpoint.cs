using DungeonForge.Application.Features.Social.Asks;
using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Asks.SendAsk;

namespace DungeonForge.Api.Features.Social.Asks.SendAsk;

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

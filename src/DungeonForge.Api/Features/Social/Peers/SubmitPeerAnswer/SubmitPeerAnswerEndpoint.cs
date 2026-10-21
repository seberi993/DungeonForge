using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Peers.SubmitPeerAnswer;

namespace DungeonForge.Api.Features.Social.Peers.SubmitPeerAnswer;

public sealed class SubmitPeerAnswerEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/social/peer-answer", async Task<IResult> (
                Guid id, SubmitPeerAnswerRequest request, SubmitPeerAnswerHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<SubmitPeerAnswerRequest>(1);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.PlayerName) || request.PlayerName.Trim().Length > MaxNameLength)
               {
                   errors["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."];
               }

               if (string.IsNullOrWhiteSpace(request?.PromptId))
               {
                   errors["promptId"] = ["Prompt id is required."];
               }

               if (request?.Choice is not { } choice || !Enum.IsDefined(choice))
               {
                   errors["choice"] = ["Choice must be yes, maybe or no."];
               }

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(SubmitPeerAnswerHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("SubmitPeerAnswer");
}

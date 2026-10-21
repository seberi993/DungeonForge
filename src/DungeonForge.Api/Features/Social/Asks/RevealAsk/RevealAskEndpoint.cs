using DungeonForge.Application.Features.Social.Asks;
using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Asks.RevealAsk;

namespace DungeonForge.Api.Features.Social.Asks.RevealAsk;

public sealed class RevealAskEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/social/asks/{askId}/reveal", async Task<IResult> (
                Guid id, string askId, RevealAskRequest request, RevealAskHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, askId, request)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<RevealAskRequest>(2);
               return string.IsNullOrWhiteSpace(request?.From) || request.From.Trim().Length > MaxNameLength
                   ? ValidationErrors.ToProblem(
                       RevealAskHandler.Operation,
                       new Dictionary<string, string[]> { ["from"] = [$"Asker is required and at most {MaxNameLength} characters."] },
                       ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("RevealAsk");
}

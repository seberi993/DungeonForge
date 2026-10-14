using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/explore", async Task<Results<Ok<ExploreResponse>, NotFound, Conflict, Conflict<WaitingForPartyResponse>, ValidationProblem>> (
                Guid id, ExploreDungeonHandler handler, CancellationToken ct, [FromBody] ExploreRequest? request = null) =>
                await handler.HandleAsync(id, request, ct) switch
                {
                    { Status: ExploreStatus.Explored, Response: { } response } => TypedResults.Ok(response),
                    { Status: ExploreStatus.Defeated } => TypedResults.Conflict(),
                    { Status: ExploreStatus.Waiting, Waiting: { } waiting } => TypedResults.Conflict(waiting),
                    { Status: ExploreStatus.InvalidRequest } r => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["actions"] = [r.Error ?? "Invalid request."] }),
                    _ => TypedResults.NotFound(),
                })
           .AddEndpointFilter(async (ctx, next) =>
               ExploreRequestLimits.Validate(ctx.Arguments.OfType<ExploreRequest>().FirstOrDefault()?.Actions) is { } error
                   ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["actions"] = [error] })
                   : await next(ctx))
           .WithName("ExploreDungeon");
}

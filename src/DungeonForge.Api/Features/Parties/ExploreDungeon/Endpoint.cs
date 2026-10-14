using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/explore", async Task<Results<Ok<ExploreResponse>, NotFound, Conflict, ValidationProblem>> (
                Guid id, ExploreDungeonHandler handler, CancellationToken ct, [FromBody] ExploreRequest? request = null) =>
                await handler.HandleAsync(id, request, ct) switch
                {
                    { Status: ExploreStatus.Explored, Response: { } response } => TypedResults.Ok(response),
                    { Status: ExploreStatus.Defeated } => TypedResults.Conflict(),
                    { Status: ExploreStatus.InvalidChoice } r => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["selectedChoiceIndex"] = [r.Error ?? "Invalid choice."] }),
                    _ => TypedResults.NotFound(),
                })
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.Arguments.OfType<ExploreRequest>().FirstOrDefault();
               return request?.SelectedChoiceIndex is < 0 or > 2
                   ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
                   {
                       ["selectedChoiceIndex"] = ["Choice index must be between 0 and 2."],
                   })
                   : await next(ctx);
           })
           .WithName("ExploreDungeon");
}

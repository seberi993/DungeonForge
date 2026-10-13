using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/explore", Results<Ok<ExploreResponse>, NotFound, Conflict> (Guid id, ExploreDungeonHandler handler) =>
                handler.Handle(id) switch
                {
                    { Status: ExploreStatus.Explored, Response: { } response } => TypedResults.Ok(response),
                    { Status: ExploreStatus.Defeated } => TypedResults.Conflict(),
                    _ => TypedResults.NotFound(),
                })
           .WithName("ExploreDungeon");
}

using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/explore", Results<Ok<ExploreResponse>, NotFound> (Guid id, ExploreDungeonHandler handler) =>
                handler.Handle(id) is { } response
                    ? TypedResults.Ok(response)
                    : TypedResults.NotFound())
           .WithName("ExploreDungeon");
}

using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.UnlockTurn;

public sealed class UnlockTurnEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/unlock", async Task<Results<Ok<UnlockTurnResponse>, NotFound>> (
                Guid id, UnlockTurnRequest request, UnlockTurnHandler handler) =>
                await handler.HandleAsync(id, request.MemberId) is { } response
                    ? TypedResults.Ok(response)
                    : TypedResults.NotFound())
           .WithName("UnlockTurn");
}

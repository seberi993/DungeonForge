using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}", Results<Ok<GetPartyStateResponse>, NotFound> (Guid id, GetPartyStateHandler handler) =>
                handler.Handle(id) is { } response
                    ? TypedResults.Ok(response)
                    : TypedResults.NotFound())
           .WithName("GetPartyState");
}

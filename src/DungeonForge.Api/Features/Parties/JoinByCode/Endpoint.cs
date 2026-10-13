using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.JoinByCode;

public sealed class JoinByCodeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/by-code/{code}", Results<Ok<GetPartyStateResponse>, NotFound> (string code, JoinByCodeHandler handler) =>
                handler.Handle(code) is { } response
                    ? TypedResults.Ok(response)
                    : TypedResults.NotFound())
           .WithName("JoinPartyByCode");
}

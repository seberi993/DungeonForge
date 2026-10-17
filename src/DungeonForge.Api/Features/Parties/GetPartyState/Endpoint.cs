using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}", IResult (Guid id, GetPartyStateHandler handler, HttpContext http) =>
                handler.Handle(id).ToHttp(http))
           .WithName("GetPartyState");
}

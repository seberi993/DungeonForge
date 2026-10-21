using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.GetPartyState;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;
using DungeonForge.Application.Errors;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}", IResult (Guid id, GetPartyStateHandler handler, HttpContext http) =>
                handler.Handle(id).ToHttp(http))
           .WithName("GetPartyState");
}

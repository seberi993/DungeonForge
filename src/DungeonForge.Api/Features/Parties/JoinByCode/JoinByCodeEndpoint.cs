using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.JoinByCode;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;
using DungeonForge.Application.Errors;

namespace DungeonForge.Api.Features.Parties.JoinByCode;

public sealed class JoinByCodeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/by-code/{code}", IResult (string code, JoinByCodeHandler handler, HttpContext http) =>
                handler.Handle(code).ToHttp(http))
           .WithName("JoinPartyByCode");
}

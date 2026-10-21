using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.GetSetup;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;
using DungeonForge.Application.Errors;

namespace DungeonForge.Api.Features.Parties.GetSetup;

public sealed class GetSetupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}/setup", (Guid id, string? playerName, GetSetupHandler handler, HttpContext http) =>
                handler.Handle(id, playerName).ToHttp(http))
           .WithName("GetSetup");
}

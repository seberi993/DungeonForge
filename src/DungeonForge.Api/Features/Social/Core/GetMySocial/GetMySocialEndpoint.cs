using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Core.GetMySocial;

namespace DungeonForge.Api.Features.Social.Core.GetMySocial;

public sealed class GetMySocialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}/social/me", IResult (Guid id, string? playerName, GetMySocialHandler handler, HttpContext http) =>
                handler.Handle(id, playerName).ToHttp(http))
           .WithName("GetMySocial");
}

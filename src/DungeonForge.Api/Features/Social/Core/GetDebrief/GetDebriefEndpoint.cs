using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Core.GetDebrief;

namespace DungeonForge.Api.Features.Social.Core.GetDebrief;

public sealed class GetDebriefEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}/social/debrief", IResult (Guid id, string? playerName, GetDebriefHandler handler, HttpContext http) =>
                handler.Handle(id, playerName).ToHttp(http))
           .WithName("GetDebrief");
}

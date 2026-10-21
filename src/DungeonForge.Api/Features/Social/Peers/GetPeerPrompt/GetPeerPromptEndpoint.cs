using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Peers.GetPeerPrompt;

namespace DungeonForge.Api.Features.Social.Peers.GetPeerPrompt;

public sealed class GetPeerPromptEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}/social/peer-prompt", IResult (Guid id, string? playerName, GetPeerPromptHandler handler, HttpContext http) =>
                handler.Handle(id, playerName).ToHttp(http))
           .WithName("GetPeerPrompt");
}

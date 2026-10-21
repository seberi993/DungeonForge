using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.UnlockTurn;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;
using DungeonForge.Application.Errors;

namespace DungeonForge.Api.Features.Parties.UnlockTurn;

public sealed class UnlockTurnEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/unlock", async Task<IResult> (
                Guid id, UnlockTurnRequest request, UnlockTurnHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request.MemberId)).ToHttp(http))
           .WithName("UnlockTurn");
}

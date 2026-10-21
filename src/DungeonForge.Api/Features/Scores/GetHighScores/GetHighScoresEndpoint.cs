using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Scores;
using DungeonForge.Application.Features.Scores.GetHighScores;
using DungeonForge.Application.Errors;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;

namespace DungeonForge.Api.Features.Scores.GetHighScores;

public sealed class GetHighScoresEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/scores", async (int? limit, GetHighScoresHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(limit, ct)))
           .WithName("GetHighScores");
}

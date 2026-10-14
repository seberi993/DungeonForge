using DungeonForge.Api.Endpoints;

namespace DungeonForge.Api.Features.Scores.GetHighScores;

public sealed class GetHighScoresEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/scores", async (int? limit, GetHighScoresHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(limit, ct)))
           .WithName("GetHighScores");
}

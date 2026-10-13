using DungeonForge.Api.Endpoints;

namespace DungeonForge.Api.Features.Health.GetHealth;

public sealed class GetHealthEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
           .WithName("GetHealth");
}

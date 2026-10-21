using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Health;
using DungeonForge.Application.Features.Health.GetHealth;
using DungeonForge.Application.Errors;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;

namespace DungeonForge.Api.Features.Health.GetHealth;

public sealed class GetHealthEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
           .WithName("GetHealth");
}

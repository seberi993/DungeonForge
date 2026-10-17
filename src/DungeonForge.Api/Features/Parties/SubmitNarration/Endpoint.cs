using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Features.Parties.SubmitNarration;

public sealed class SubmitNarrationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/narration", async Task<IResult> (
                Guid id, SubmitNarrationRequest request, ClientNarrationCoordinator coordinator, HttpContext http) =>
            {
                if (request.Text is { Length: > SubmitNarrationLimits.MaxTextLength })
                {
                    return ErrorMapping.ToProblem(
                        NarrationErrors.TextTooLong(ClientNarrationCoordinator.SubmitNarrationOperation, id, request.Turn), http);
                }

                var result = await coordinator.SubmitAsync(id, request.Turn, request.Text, request.Declined);
                return result.Match<IResult>(() => TypedResults.NoContent(), error => ErrorMapping.ToProblem(error, http));
            })
           .WithName("SubmitNarration");
}

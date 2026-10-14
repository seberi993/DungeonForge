using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.SubmitNarration;

public sealed class SubmitNarrationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/narration", async Task<Results<NoContent, NotFound, Conflict, ValidationProblem>> (
                Guid id, SubmitNarrationRequest request, ExploreDungeonHandler handler) =>
            {
                if (request.Text is { Length: > SubmitNarrationLimits.MaxTextLength })
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["text"] = [$"Text must be at most {SubmitNarrationLimits.MaxTextLength} characters."],
                    });
                }

                return await handler.SubmitClientNarrationAsync(id, request.Turn, request.Text, request.Declined) switch
                {
                    ClientNarrationStatus.Accepted => TypedResults.NoContent(),
                    ClientNarrationStatus.NotFound => (Results<NoContent, NotFound, Conflict, ValidationProblem>)TypedResults.NotFound(),
                    _ => TypedResults.Conflict(),
                };
            })
           .WithName("SubmitNarration");
}

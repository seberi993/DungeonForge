using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.ExtractRun;

public sealed class ExtractRunEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/extract", async Task<Results<Ok<ExtractResponse>, NotFound, Conflict, ValidationProblem>> (
                Guid id, ExtractRequest request, ExtractRunHandler handler) =>
                await handler.HandleAsync(id, request.PlayerName!) switch
                {
                    { Status: ExtractStatus.Extracted, Response: { } response } => TypedResults.Ok(response),
                    { Status: ExtractStatus.NoPact } => TypedResults.Conflict(),
                    { Status: ExtractStatus.UnknownPlayer } => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["playerName"] = ["No active player with that name."] }),
                    _ => TypedResults.NotFound(),
                })
           .AddEndpointFilter(async (ctx, next) =>
           {
               var name = ctx.GetArgument<ExtractRequest>(1)?.PlayerName;
               return string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength
                   ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
                   {
                       ["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."],
                   })
                   : await next(ctx);
           })
           .WithName("ExtractRun");
}

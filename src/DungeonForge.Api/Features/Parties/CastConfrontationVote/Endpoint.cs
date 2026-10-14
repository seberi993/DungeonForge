using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.CastConfrontationVote;

public sealed class CastConfrontationVoteEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/confrontation/votes", async Task<Results<Ok<CastVoteResponse>, NotFound, Conflict, ValidationProblem>> (
                Guid id, CastVoteRequest request, CastConfrontationVoteHandler handler) =>
                await handler.HandleAsync(id, request.PlayerName!, request.Vote!.Value) switch
                {
                    { Status: CastVoteStatus.Recorded, Response: { } response } => TypedResults.Ok(response),
                    { Status: CastVoteStatus.NoConfrontation } => TypedResults.Conflict(),
                    { Status: CastVoteStatus.NotEligible } => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["playerName"] = ["That player cannot vote in this confrontation."] }),
                    _ => TypedResults.NotFound(),
                })
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<CastVoteRequest>(1);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.PlayerName) || request.PlayerName.Trim().Length > MaxNameLength)
               {
                   errors["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."];
               }

               if (request?.Vote is not { } vote || !Enum.IsDefined(vote))
               {
                   errors["vote"] = ["Vote must be forgive, demandCut, banish or attack."];
               }

               return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : await next(ctx);
           })
           .WithName("CastConfrontationVote");
}

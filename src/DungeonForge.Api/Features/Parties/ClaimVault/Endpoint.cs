using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.ClaimVault;

public sealed class ClaimVaultEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/vault/claim", async Task<Results<Ok<ClaimVaultResponse>, NotFound, Conflict, ValidationProblem>> (
                Guid id, ClaimVaultRequest request, ClaimVaultHandler handler) =>
                await handler.HandleAsync(id, request.PlayerName!, request.Choice!.Value) switch
                {
                    { Status: ClaimVaultStatus.Claimed, Response: { } response } => TypedResults.Ok(response),
                    { Status: ClaimVaultStatus.NoVault or ClaimVaultStatus.ConfrontationActive } => TypedResults.Conflict(),
                    { Status: ClaimVaultStatus.UnknownPlayer } => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["playerName"] = ["No active player with that name."] }),
                    _ => TypedResults.NotFound(),
                })
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<ClaimVaultRequest>(1);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.PlayerName) || request.PlayerName.Trim().Length > MaxNameLength)
               {
                   errors["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."];
               }

               if (request?.Choice is not { } choice || !Enum.IsDefined(choice))
               {
                   errors["choice"] = ["Choice must be share or pocket."];
               }

               return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : await next(ctx);
           })
           .WithName("ClaimVault");
}

using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.ClaimVault;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;
using DungeonForge.Application.Errors;
using DungeonForge.Shared;

namespace DungeonForge.Api.Features.Parties.ClaimVault;

public sealed class ClaimVaultEndpoint : IEndpoint
{
    private const int MaxNameLength = Limits.MaxPlayerNameLength;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/vault/claim", async Task<IResult> (
                Guid id, ClaimVaultRequest request, ClaimVaultHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request.PlayerName!, request.Choice!.Value)).ToHttp(http))
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

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(ClaimVaultHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("ClaimVault");
}

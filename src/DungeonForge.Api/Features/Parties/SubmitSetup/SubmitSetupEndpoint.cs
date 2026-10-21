using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.SubmitSetup;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;
using DungeonForge.Application.Errors;

namespace DungeonForge.Api.Features.Parties.SubmitSetup;

public sealed class SubmitSetupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/setup", async Task<IResult> (
                Guid id, SubmitSetupRequest request, SubmitSetupHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<SubmitSetupRequest>(1);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.PlayerName))
               {
                   errors["playerName"] = ["A player name is required."];
               }

               if (string.IsNullOrWhiteSpace(request?.Text))
               {
                   errors["text"] = ["Tell the DM something."];
               }

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(SubmitSetupHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("SubmitSetup");
}

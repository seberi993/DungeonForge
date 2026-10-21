using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Seed.FinalizeProfiles;

namespace DungeonForge.Api.Features.Social.Seed.FinalizeProfiles;

public sealed class FinalizeProfilesEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/social/finalize", async Task<IResult> (
                Guid id, FinalizeProfilesRequest request, FinalizeProfilesHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<FinalizeProfilesRequest>(1);
               return string.IsNullOrWhiteSpace(request?.PlayerName) || request.PlayerName.Trim().Length > MaxNameLength
                   ? ValidationErrors.ToProblem(
                       FinalizeProfilesHandler.Operation,
                       new Dictionary<string, string[]> { ["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."] },
                       ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("FinalizeProfiles");
}

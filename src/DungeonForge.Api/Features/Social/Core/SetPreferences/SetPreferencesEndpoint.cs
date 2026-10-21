using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Core.SetPreferences;

namespace DungeonForge.Api.Features.Social.Core.SetPreferences;

public sealed class SetPreferencesEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/social/preferences", IResult (Guid id, SetPreferencesRequest request, SetPreferencesHandler handler, HttpContext http) =>
                handler.Handle(id, request).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<SetPreferencesRequest>(1);
               return string.IsNullOrWhiteSpace(request?.PlayerName) || request.PlayerName.Trim().Length > MaxNameLength
                   ? ValidationErrors.ToProblem(
                       SetPreferencesHandler.Operation,
                       new Dictionary<string, string[]> { ["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."] },
                       ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("SetPreferences");
}

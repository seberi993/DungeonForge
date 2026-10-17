using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.ExtractRun;

public sealed class ExtractRunEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/extract", async Task<IResult> (
                Guid id, ExtractRequest request, ExtractRunHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request.PlayerName!)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var name = ctx.GetArgument<ExtractRequest>(1)?.PlayerName;
               return string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength
                   ? ValidationErrors.ToProblem(ExtractRunHandler.Operation, new Dictionary<string, string[]>
                   {
                       ["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."],
                   }, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("ExtractRun");
}

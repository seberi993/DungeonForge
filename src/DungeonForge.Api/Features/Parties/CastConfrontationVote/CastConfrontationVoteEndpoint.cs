using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.CastConfrontationVote;
using DungeonForge.Api.Http;
using DungeonForge.Api.Endpoints;
using DungeonForge.Application.Errors;
using DungeonForge.Shared;

namespace DungeonForge.Api.Features.Parties.CastConfrontationVote;

public sealed class CastConfrontationVoteEndpoint : IEndpoint
{
    private const int MaxNameLength = Limits.MaxPlayerNameLength;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/confrontation/votes", async Task<IResult> (
                Guid id, CastVoteRequest request, CastConfrontationVoteHandler handler, HttpContext http) =>
                (await handler.HandleAsync(id, request.PlayerName!, request.Vote!.Value)).ToHttp(http))
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

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(CastConfrontationVoteHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("CastConfrontationVote");
}

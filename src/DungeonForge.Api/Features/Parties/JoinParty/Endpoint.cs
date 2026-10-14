using DungeonForge.Api.Domain;
using DungeonForge.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DungeonForge.Api.Features.Parties.JoinParty;

public sealed class JoinPartyEndpoint : IEndpoint
{
    private const int MaxMemberNameLength = 32;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/by-code/{code}/join", async Task<Results<Ok<JoinPartyResponse>, NotFound, Conflict>> (
                string code, JoinPartyRequest request, JoinPartyHandler handler) =>
                await handler.HandleAsync(code, request) switch
                {
                    { Status: JoinPartyStatus.Joined, Response: { } response } => TypedResults.Ok(response),
                    { Status: JoinPartyStatus.Conflict } => TypedResults.Conflict(),
                    _ => TypedResults.NotFound(),
                })
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<JoinPartyRequest>(1);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.Name))
               {
                   errors["name"] = ["Name is required."];
               }
               else if (request.Name.Trim().Length > MaxMemberNameLength)
               {
                   errors["name"] = [$"Name must be at most {MaxMemberNameLength} characters."];
               }

               if (request?.Class is not { } cls || !Enum.IsDefined(cls))
               {
                   errors["class"] = ["A valid class is required."];
               }

               return errors.Count > 0
                   ? TypedResults.ValidationProblem(errors)
                   : await next(ctx);
           })
           .WithName("JoinParty");
}

using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Shared;

namespace DungeonForge.Api.Features.Parties.JoinParty;

public sealed class JoinPartyEndpoint : IEndpoint
{
    private const int MaxMemberNameLength = Limits.MaxMemberNameLength;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/by-code/{code}/join", async Task<IResult> (
                string code, JoinPartyRequest request, JoinPartyHandler handler, HttpContext http) =>
                (await handler.HandleAsync(code, request)).ToHttp(http))
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

               if (request?.Class is { } cls && !Enum.IsDefined(cls))
               {
                   errors["class"] = ["That class does not exist."];
               }

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(JoinPartyHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("JoinParty");
}

using DungeonForge.Api.Endpoints;

namespace DungeonForge.Api.Features.Parties.CreateParty;

public sealed class CreatePartyEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties", (CreatePartyRequest request, CreatePartyHandler handler) =>
                TypedResults.Ok(handler.Handle(request)))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<CreatePartyRequest>(0);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.Name))
               {
                   errors["name"] = ["Name is required."];
               }

               if (request?.Members is not { Count: > 0 } members)
               {
                   errors["members"] = ["At least one member is required."];
               }
               else if (members.Any(m => m is null
                   || string.IsNullOrWhiteSpace(m.Name)
                   || m.Class is not { } cls
                   || !Enum.IsDefined(cls)))
               {
                   errors["members"] = ["Every member needs a name and a valid class."];
               }

               return errors.Count > 0
                   ? TypedResults.ValidationProblem(errors)
                   : await next(ctx);
           })
           .WithName("CreateParty");
}

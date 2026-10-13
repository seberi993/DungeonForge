using DungeonForge.Api.Endpoints;

namespace DungeonForge.Api.Features.Parties.CreateParty;

public sealed class CreatePartyEndpoint : IEndpoint
{
    private const int MaxPartyNameLength = 40;
    private const int MaxMemberNameLength = 32;
    private const int MaxMembers = 8;

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
               else if (request.Name.Trim().Length > MaxPartyNameLength)
               {
                   errors["name"] = [$"Name must be at most {MaxPartyNameLength} characters."];
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

               else if (members.Count > MaxMembers)
               {
                   errors["members"] = [$"A party can have at most {MaxMembers} members."];
               }
               else if (members.Any(m => m.Name!.Trim().Length > MaxMemberNameLength))
               {
                   errors["members"] = [$"Member names must be at most {MaxMemberNameLength} characters."];
               }
               else if (members.Select(m => m.Name!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count)
               {
                   errors["members"] = ["Member names must be unique."];
               }

               return errors.Count > 0
                   ? TypedResults.ValidationProblem(errors)
                   : await next(ctx);
           })
           .WithName("CreateParty");
}

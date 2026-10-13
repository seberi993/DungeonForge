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
               if (string.IsNullOrWhiteSpace(request?.Name))
               {
                   return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                   {
                       ["name"] = ["Name is required."],
                   });
               }
               return await next(ctx);
           })
           .WithName("CreateParty");
}

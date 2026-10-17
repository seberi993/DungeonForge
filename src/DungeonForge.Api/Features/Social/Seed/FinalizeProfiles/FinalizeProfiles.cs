using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Social.Core;

namespace DungeonForge.Api.Features.Social.Seed.FinalizeProfiles;

public sealed record FinalizeProfilesRequest(string? PlayerName);

public sealed record FinalizeProfilesResponse(bool Finalized);

// Starts the run without waiting for stragglers: anyone who has not answered keeps a neutral profile and still gets gifts.
public sealed class FinalizeProfilesHandler(SocialAccess access, SocialLifecycle lifecycle, ChaosDispatcher chaos)
{
    public const string Operation = "Command:FinalizeProfiles";

    public async Task<Result<FinalizeProfilesResponse>> HandleAsync(Guid id, FinalizeProfilesRequest request)
    {
        var resolved = access.Resolve(id, request.PlayerName, SocialModule.Seed, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, _) = resolved.Value;
        var finalizedNow = lifecycle.TryFinalize(party, force: true);
        access.Touch(party);
        if (finalizedNow)
        {
            await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.Narrator, "The dungeon has measured you all. Gifts stir in the dark."));
        }

        return new FinalizeProfilesResponse(true);
    }
}

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

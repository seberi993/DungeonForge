using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Social.Core.SetPreferences;

// Each field is optional: send only what changes.
public sealed record SetPreferencesRequest(string? PlayerName, bool? OptOut, bool? DebriefOptIn);

public sealed record PreferencesResponse(bool OptOut, bool DebriefOptIn);

// Opting out takes a player out of the social layer: nobody is asked about them, they are asked about nobody, and no
// whisper can reach them or leave them. The debrief opt-in lets them see their exact hidden profile once the run is over.
public sealed class SetPreferencesHandler(SocialAccess access)
{
    public const string Operation = "Command:SetPreferences";

    public Result<PreferencesResponse> Handle(Guid id, SetPreferencesRequest request)
    {
        var resolved = access.Resolve(id, request.PlayerName, module: null, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        lock (party.SyncRoot)
        {
            var profile = party.Social().Profile(member.Name);
            if (request.OptOut is { } optOut) profile.OptedOut = optOut;
            if (request.DebriefOptIn is { } debrief) profile.DebriefOptIn = debrief;
            access.Touch(party);
            return new PreferencesResponse(profile.OptedOut, profile.DebriefOptIn);
        }
    }
}

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

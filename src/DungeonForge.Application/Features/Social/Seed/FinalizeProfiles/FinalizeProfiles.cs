using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Social.Core;

namespace DungeonForge.Application.Features.Social.Seed.FinalizeProfiles;

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

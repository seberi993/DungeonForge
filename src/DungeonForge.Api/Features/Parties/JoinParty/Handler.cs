using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Realtime;

namespace DungeonForge.Api.Features.Parties.JoinParty;

// Adds the joiner's hero to an existing party and tells everyone already connected.
public sealed class JoinPartyHandler(IPartyStore parties, IPartyNotifier notifier, ILogger<JoinPartyHandler> logger)
{
    public const string Operation = "Command:JoinParty";

    public async Task<Result<JoinPartyResponse>> HandleAsync(string? code, JoinPartyRequest request)
    {
        if (InviteCodes.Normalize(code) is not { } normalized || !parties.TryGetByInvite(normalized, out var party))
        {
            return PartyErrors.InviteNotFound(Operation, code);
        }

        var id = party.Id;

        // Request was validated by the endpoint filter.
        var name = request.Name!.Trim();
        GetPartyStateResponse state;
        lock (party.SyncRoot)
        {
            if (!party.TryAddMember(new PartyMember(name, request.Class!.Value)))
            {
                // The domain only says "no"; both causes were a 409 before, and the roster size tells them apart.
                return party.Members.Count >= PartyRules.MaxMembers
                    ? JoinPartyErrors.Full(Operation, id)
                    : JoinPartyErrors.NameTaken(Operation, id, name);
            }

            state = PartyMapping.ToResponse(party.Snapshot());
        }

        parties.Save(party);
        await BestEffort.RunAsync(
            logger, Operation, id, "PartyUpdated", () => notifier.PartyUpdatedAsync(state, CancellationToken.None));

        return new JoinPartyResponse(name, state);
    }
}

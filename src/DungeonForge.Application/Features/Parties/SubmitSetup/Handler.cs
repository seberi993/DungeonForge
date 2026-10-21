using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Narration;
using DungeonForge.Shared;

namespace DungeonForge.Application.Features.Parties.SubmitSetup;

// Command: one player's free-text answer to the DM's opening question. The answers become the first scenario's canon.
public sealed class SubmitSetupHandler(IPartyStore parties, IPartyNotifier notifier, ILogger<SubmitSetupHandler> logger)
{
    public const string Operation = "Command:SubmitSetup";

    public async Task<Result<SubmitSetupResponse>> HandleAsync(Guid id, SubmitSetupRequest request)
    {
        if (!parties.TryGet(id, out var party))
        {
            return PartyErrors.NotFound(Operation, id);
        }

        // Request was validated by the endpoint filter.
        var name = request.PlayerName!.Trim();
        var text = NarrationText.Clean(request.Text, Limits.MaxSetupAnswerLength);
        GetPartyStateResponse state;
        SubmitSetupResponse response;
        lock (party.SyncRoot)
        {
            var outcome = party.TrySubmitSetup(name, text, request.ForceStart);
            if (outcome != SetupSubmit.Accepted)
            {
                return outcome switch
                {
                    SetupSubmit.UnknownPlayer => PartyErrors.PlayerUnknown(Operation, id, name),
                    SetupSubmit.AlreadyAnswered => SetupErrors.AlreadyAnswered(Operation, id, name),
                    _ => SetupErrors.NotInSetup(Operation, id),
                };
            }

            state = PartyMapping.ToResponse(party.Snapshot());
            response = new SubmitSetupResponse(id, state.SetupAnswered, state.Members.Count, state.SetupPending);
        }

        parties.Save(party);
        await BestEffort.RunAsync(
            logger, Operation, id, "PartyUpdated", () => notifier.PartyUpdatedAsync(state, CancellationToken.None));
        return response;
    }
}

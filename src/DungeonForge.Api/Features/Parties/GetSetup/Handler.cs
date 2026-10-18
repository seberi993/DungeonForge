using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.GetSetup;

// Query: the DM's opening question for one player, and how far the party is in painting the scene.
public sealed class GetSetupHandler(IPartyStore parties)
{
    public const string Operation = "Query:GetSetup";

    public Result<GetSetupResponse> Handle(Guid id, string? playerName)
    {
        if (!parties.TryGet(id, out var party))
        {
            return PartyErrors.NotFound(Operation, id);
        }

        lock (party.SyncRoot)
        {
            var name = playerName?.Trim() ?? string.Empty;
            var index = party.Members.ToList().FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            return index < 0
                ? PartyErrors.PlayerUnknown(Operation, id, name)
                : new GetSetupResponse(
                    id, SetupQuestions.For(index), party.HasAnsweredSetup(name), party.SetupAnswered, party.Members.Count, party.SetupPending);
        }
    }
}

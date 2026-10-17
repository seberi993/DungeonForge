using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed class GetPartyStateHandler(IPartyStore parties)
{
    public const string Operation = "Query:GetPartyState";

    public Result<GetPartyStateResponse> Handle(Guid id) =>
        parties.TryGet(id, out var party)
            ? PartyMapping.ToResponse(party.Snapshot())
            : PartyErrors.NotFound(Operation, id);
}

using DungeonForge.Api.Domain;

namespace DungeonForge.Api.Features.Parties.CreateParty;

public sealed class CreatePartyHandler(IPartyStore parties, TimeProvider time)
{
    // Single-instance host: this lock makes code-uniqueness check + reservation atomic.
    private static readonly Lock InviteGate = new();

    public PartyResponse Handle(CreatePartyRequest request)
    {
        var id = Guid.NewGuid();
        string code;
        lock (InviteGate)
        {
            do
            {
                code = InviteCodes.Generate();
            }
            while (parties.InviteCodeTaken(code));

            // Request was validated by the endpoint filter.
            var members = request.Members!.Select((m, i) => new PartyMember(m.Name!.Trim(), m.Class ?? HiddenClasses.For(i))).ToList();
            var party = Party.Create(id, request.Name!.Trim(), members, time.GetUtcNow(), code, request.Modifiers, time);

            if (request.PaintScene)
            {
                party.RequireSetup();
            }

            parties.Register(party, code);

            var snapshot = party.Snapshot();
            return new PartyResponse(snapshot.Id, snapshot.Name, PartyMapping.ToMemberDtos(snapshot), snapshot.CreatedAt, InviteCodes.Format(code), party.SetupPending);
        }
    }
}

namespace DungeonForge.Api.Features.Parties.CreateParty;

public sealed record CreatePartyRequest(string Name);

public sealed record PartyResponse(Guid PartyId, string Name, IReadOnlyList<string> Members, DateTimeOffset CreatedAt);

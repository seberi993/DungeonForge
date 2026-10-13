namespace DungeonForge.Api.Features.Parties.GetPartyState;

public sealed record GetPartyStateResponse(Guid PartyId, string Name, IReadOnlyList<string> Members, DateTimeOffset CreatedAt, int Hp, int Gold, int Turn);

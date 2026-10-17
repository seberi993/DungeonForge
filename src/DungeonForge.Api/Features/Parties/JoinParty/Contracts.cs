using DungeonForge.Api.Domain;

namespace DungeonForge.Api.Features.Parties.JoinParty;

public sealed record JoinPartyRequest(string? Name, CharacterClass? Class);

public sealed record JoinPartyResponse(string MemberName, GetPartyStateResponse State);

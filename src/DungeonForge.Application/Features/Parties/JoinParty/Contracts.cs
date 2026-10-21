using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Parties.JoinParty;

public sealed record JoinPartyRequest(string? Name, CharacterClass? Class);

public sealed record JoinPartyResponse(string MemberName, GetPartyStateResponse State);

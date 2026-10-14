using DungeonForge.Api.Domain;

namespace DungeonForge.Api.Features.Parties.CreateParty;

public sealed record MemberRequest(string? Name, CharacterClass? Class);

public sealed record CreatePartyRequest(string? Name, IReadOnlyList<MemberRequest>? Members, PartyModifiers? Modifiers = null);

public sealed record PartyResponse(Guid PartyId, string Name, IReadOnlyList<MemberDto> Members, DateTimeOffset CreatedAt, string InviteCode = "");

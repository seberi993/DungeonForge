using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Shared;
namespace DungeonForge.Web.Features.Lobby;

public sealed record MemberRequest(string Name, CharacterClass? Class = null);

public sealed record CreatePartyRequest(string Name, IReadOnlyList<MemberRequest> Members, bool PaintScene = true);

public sealed record PartyResponse(Guid PartyId, string Name, IReadOnlyList<MemberDto> Members, DateTimeOffset CreatedAt, string InviteCode = "", bool SetupPending = false);

public sealed record JoinPartyRequest(string Name, CharacterClass? Class = null);

public sealed record JoinPartyResponse(string MemberName, PartyStateDto State);

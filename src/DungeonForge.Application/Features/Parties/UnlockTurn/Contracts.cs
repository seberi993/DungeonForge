namespace DungeonForge.Application.Features.Parties.UnlockTurn;

public sealed record UnlockTurnRequest(Guid MemberId);

public sealed record UnlockTurnResponse(Guid PartyId, IReadOnlyList<LockStatusDto> LockStatus);

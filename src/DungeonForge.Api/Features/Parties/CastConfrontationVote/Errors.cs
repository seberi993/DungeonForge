using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.CastConfrontationVote;

internal static class CastConfrontationVoteErrors
{
    private static readonly ErrorSpec NoConfrontationSpec = new(
        ErrorCodes.Vote.NoConfrontation, ErrorKind.Conflict, "There is no open confrontation to vote on.");

    // Field keeps today's 400 body: errors.playerName.
    private static readonly ErrorSpec NotEligibleSpec = new(
        ErrorCodes.Vote.NotEligible, ErrorKind.Validation, "That player cannot vote in this confrontation.", Field: "playerName");

    public static Error NoConfrontation(string operation, Guid partyId) => NoConfrontationSpec.Create(operation, Param.PartyId(partyId));

    public static Error NotEligible(string operation, Guid partyId, string playerName) =>
        NotEligibleSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(playerName));
}

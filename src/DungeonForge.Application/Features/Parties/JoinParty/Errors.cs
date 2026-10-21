using DungeonForge.Application.Errors;

namespace DungeonForge.Application.Features.Parties.JoinParty;

internal static class JoinPartyErrors
{
    private static readonly ErrorSpec FullSpec = new(
        ErrorCodes.Party.Full, ErrorKind.Conflict, "The party already has the maximum number of members.");

    private static readonly ErrorSpec NameTakenSpec = new(
        ErrorCodes.Party.NameTaken, ErrorKind.Conflict, "A member with that name is already in the party.");

    public static Error Full(string operation, Guid partyId) => FullSpec.Create(operation, Param.PartyId(partyId));

    public static Error NameTaken(string operation, Guid partyId, string name) =>
        NameTakenSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));
}

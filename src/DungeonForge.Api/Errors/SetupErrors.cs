namespace DungeonForge.Api.Errors;

// Errors the opening-scene phase raises, shared by the setup slices and the scenario gate.
public static class SetupErrors
{
    private static readonly ErrorSpec PendingSpec = new(
        ErrorCodes.Setup.Pending, ErrorKind.Conflict, "The party is still painting the opening scene; every player must answer first.");

    private static readonly ErrorSpec NotInSetupSpec = new(
        ErrorCodes.Setup.NotInSetup, ErrorKind.Conflict, "This party is not painting an opening scene.");

    private static readonly ErrorSpec AlreadyAnsweredSpec = new(
        ErrorCodes.Setup.AlreadyAnswered, ErrorKind.Conflict, "That player has already answered the opening question.");

    public static Error Pending(string operation, Guid partyId) => PendingSpec.Create(operation, Param.PartyId(partyId));

    public static Error NotInSetup(string operation, Guid partyId) => NotInSetupSpec.Create(operation, Param.PartyId(partyId));

    public static Error AlreadyAnswered(string operation, Guid partyId, string playerName) =>
        AlreadyAnsweredSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(playerName));
}

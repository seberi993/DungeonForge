namespace DungeonForge.Application.Errors;

// Errors several party slices share. Slice-specific errors live in that slice's Errors.cs.
public static class PartyErrors
{
    private static readonly ErrorSpec NotFoundSpec = new(
        ErrorCodes.Party.NotFound, ErrorKind.NotFound, "No party with that id exists, or it has expired.");

    private static readonly ErrorSpec InviteNotFoundSpec = new(
        ErrorCodes.Party.InviteNotFound, ErrorKind.NotFound, "No party matches that invite code, or it has expired.");

    // Field keeps today's 400 body: errors.playerName.
    private static readonly ErrorSpec PlayerUnknownSpec = new(
        ErrorCodes.Party.PlayerUnknown, ErrorKind.Validation, "No active player with that name.", Field: "playerName");

    public static Error PlayerUnknown(string operation, Guid partyId, string playerName) =>
        PlayerUnknownSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(playerName));

    public static Error NotFound(string operation, Guid partyId) => NotFoundSpec.Create(operation, Param.PartyId(partyId));

    public static Error InviteNotFound(string operation, string? code) => InviteNotFoundSpec.Create(operation, Param.Code(code ?? string.Empty));
}

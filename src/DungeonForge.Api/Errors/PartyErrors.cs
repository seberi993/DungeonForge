namespace DungeonForge.Api.Errors;

// Errors several party slices share. Slice-specific errors live in that slice's Errors.cs.
public static class PartyErrors
{
    private static readonly ErrorSpec NotFoundSpec = new(
        ErrorCodes.Party.NotFound, ErrorKind.NotFound, "No party with that id exists, or it has expired.");

    private static readonly ErrorSpec InviteNotFoundSpec = new(
        ErrorCodes.Party.InviteNotFound, ErrorKind.NotFound, "No party matches that invite code, or it has expired.");

    public static Error NotFound(string operation, Guid partyId) => NotFoundSpec.Create(operation, Param.PartyId(partyId));

    public static Error InviteNotFound(string operation, string? code) => InviteNotFoundSpec.Create(operation, Param.Code(code ?? string.Empty));
}

namespace DungeonForge.Api.Errors;

// Errors several party slices share. Slice-specific errors live in that slice's Errors.cs.
public static class PartyErrors
{
    private static readonly ErrorSpec NotFoundSpec = new(
        ErrorCodes.Party.NotFound, ErrorKind.NotFound, "No party with that id exists, or it has expired.");

    public static Error NotFound(string operation, Guid partyId) => NotFoundSpec.Create(operation, Param.PartyId(partyId));
}

using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.ExtractRun;

internal static class ExtractRunErrors
{
    private static readonly ErrorSpec NoPactSpec = new(
        ErrorCodes.Extract.NoPact, ErrorKind.Conflict, "Extraction only exists in a party playing with a Traitor Pact.");

    public static Error NoPact(string operation, Guid partyId) => NoPactSpec.Create(operation, Param.PartyId(partyId));
}

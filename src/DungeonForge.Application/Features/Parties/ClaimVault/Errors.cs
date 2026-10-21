using DungeonForge.Application.Errors;

namespace DungeonForge.Application.Features.Parties.ClaimVault;

internal static class ClaimVaultErrors
{
    private static readonly ErrorSpec NoVaultSpec = new(
        ErrorCodes.Vault.NoVault, ErrorKind.Conflict, "There is no unclaimed vault to claim.");

    private static readonly ErrorSpec ConfrontationActiveSpec = new(
        ErrorCodes.Vault.ConfrontationActive, ErrorKind.Conflict, "A confrontation is open; settle the vote before claiming.");

    public static Error NoVault(string operation, Guid partyId) => NoVaultSpec.Create(operation, Param.PartyId(partyId));

    public static Error ConfrontationActive(string operation, Guid partyId) =>
        ConfrontationActiveSpec.Create(operation, Param.PartyId(partyId));
}

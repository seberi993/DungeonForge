using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Parties.ClaimVault;

// Choice: share (declare it to the party) or pocket (take it secretly and hope nobody notices).
public sealed record ClaimVaultRequest(string? PlayerName, VaultChoice? Choice);

// Outcome: "shared", "pocketed" (unseen) or "caught" (a public confrontation vote has started). Only the claimant sees this.
public sealed record ClaimVaultResponse(Guid PartyId, string Outcome, int Gold, string? Artifact, IReadOnlyList<string>? Spotters = null);

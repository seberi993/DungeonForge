namespace DungeonForge.Application.Features.Parties.ExtractRun;

public sealed record ExtractRequest(string? PlayerName);

// Outcome: "none" (the run goes on), "traitorWinsWipe" or "traitorWinsSoloExtraction".
public sealed record ExtractResponse(Guid PartyId, string Outcome);

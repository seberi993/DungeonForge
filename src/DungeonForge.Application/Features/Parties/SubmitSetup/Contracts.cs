namespace DungeonForge.Application.Features.Parties.SubmitSetup;

public sealed record SubmitSetupRequest(string? PlayerName, string? Text, bool ForceStart = false);

public sealed record SubmitSetupResponse(Guid PartyId, int AnsweredCount, int Total, bool Pending);

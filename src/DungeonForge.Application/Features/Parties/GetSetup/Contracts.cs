namespace DungeonForge.Application.Features.Parties.GetSetup;

public sealed record GetSetupResponse(Guid PartyId, string Question, bool Answered, int AnsweredCount, int Total, bool Pending);

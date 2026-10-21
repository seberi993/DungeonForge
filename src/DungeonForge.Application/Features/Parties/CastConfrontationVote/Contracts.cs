using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Parties.CastConfrontationVote;

// Ballots are secret: the public sees who has voted only as a count, and the tally only after the verdict.
public sealed record CastVoteRequest(string? PlayerName, ConfrontationVote? Vote);

public sealed record CastVoteResponse(Guid PartyId, bool Resolved, int Cast, int Required, string? Outcome = null);

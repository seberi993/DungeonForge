using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Application.Features.Social.Supervision;
using Microsoft.Extensions.Options;

namespace DungeonForge.Application.Features.Social.Peers.SubmitPeerAnswer;

public sealed record SubmitPeerAnswerRequest(string? PlayerName, string? PromptId, PeerChoice? Choice);

// Deliberately says nothing about whether the round resolved or what it did: the effect on the subject stays hidden.
public sealed record SubmitPeerAnswerResponse(bool Accepted = true);

public sealed class SubmitPeerAnswerHandler(
    SocialAccess access, SocialPipeline pipeline, SocialDelivery delivery, IOptions<PeerParams> options)
{
    public const string Operation = "Command:SubmitPeerAnswer";

    public async Task<Result<SubmitPeerAnswerResponse>> HandleAsync(Guid id, SubmitPeerAnswerRequest request)
    {
        var resolved = access.Resolve(id, request.PlayerName, SocialModule.Peers, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        var p = options.Value;
        SocialEvent? unanimous = null;
        lock (party.SyncRoot)
        {
            var social = party.Social();
            var round = social.PeerAssignments.TryGetValue(member.Name, out var assigned) && assigned == request.PromptId
                ? social.PeerRounds.FirstOrDefault(r => r.Id == assigned)
                : null;
            if (round is null || round.Resolved || round.Responses.ContainsKey(member.Name))
            {
                return SocialErrors.PeerPromptUnknown(Operation, id, member.Name);
            }

            round.Responses[member.Name] = request.Choice!.Value;
            social.Count(SocialMetrics.PeerAnswers);
            social.PeerAssignments.Remove(member.Name);

            var others = party.Members.Count(m => !string.Equals(m.Name, round.About, StringComparison.OrdinalIgnoreCase));
            if (round.Responses.Count >= Math.Min(p.ResolveAt, others))
            {
                unanimous = Resolve(party, round, p);
            }
        }

        if (unanimous is not null)
        {
            pipeline.Process(party, unanimous);
        }

        access.Touch(party);
        await delivery.DeliverAsync(party);
        return new SubmitPeerAnswerResponse();
    }

    // Applies the round to the subject under the peer cap. Returns the unanimity event when there was one.
    private SocialEvent? Resolve(Party party, PeerRound round, PeerParams p)
    {
        var social = party.Social();
        round.Resolved = true;
        social.Count(SocialMetrics.PeerRoundsResolved);
        var question = PeerQuestions.Find(round.QuestionId)!;
        var rng = SeededRng.For(social.RunSeed, $"peer:{round.Id}");
        var result = PeerAggregator.Aggregate(question, [.. round.Responses.Values], p, rng);

        var subject = social.Profile(round.About);
        foreach (var (axis, delta) in result.Deltas)
        {
            var applied = subject.PeerShift.Add(axis, delta, p.TraitCap);
            subject.Axes.Add(axis, applied);
        }

        return result.Unanimous
            ? pipeline.NewEvent(party, SocialEventTypes.PeerUnanimous, party.Turn, [round.About],
                new Dictionary<string, string> { ["direction"] = result.Score > 0 ? "yes" : "no", ["question"] = round.QuestionId })
            : null;
    }
}

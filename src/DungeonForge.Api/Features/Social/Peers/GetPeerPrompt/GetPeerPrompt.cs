using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Social.Core;
using Microsoft.Extensions.Options;

namespace DungeonForge.Api.Features.Social.Peers.GetPeerPrompt;

// The question about one teammate. Only the subject's name and the wording leave the server: never other raters' answers.
public sealed record PeerPromptResponse(string PromptId, string Question, string About, IReadOnlyList<PeerChoice> Options, int Remaining);

public sealed class GetPeerPromptHandler(SocialAccess access, IOptions<PeerParams> options)
{
    public const string Operation = "Query:GetPeerPrompt";

    public Result<PeerPromptResponse> Handle(Guid id, string? playerName)
    {
        var resolved = access.Resolve(id, playerName, SocialModule.Peers, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        var p = options.Value;
        lock (party.SyncRoot)
        {
            if (PeerPrompting.NextFor(party, member.Name, p) is not { } round)
            {
                return SocialErrors.NoPeerPrompt(Operation, id, member.Name);
            }

            var question = PeerQuestions.Find(round.QuestionId)!;
            var remaining = Math.Max(0, PeerPrompting.Allowance(party, p) - PeerPrompting.Answered(party.Social(), member.Name));
            access.Touch(party);
            return new PeerPromptResponse(round.Id, question.TextFor(round.About), round.About, [PeerChoice.Yes, PeerChoice.Maybe, PeerChoice.No], remaining);
        }
    }
}

public sealed class GetPeerPromptEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/parties/{id:guid}/social/peer-prompt", IResult (Guid id, string? playerName, GetPeerPromptHandler handler, HttpContext http) =>
                handler.Handle(id, playerName).ToHttp(http))
           .WithName("GetPeerPrompt");
}

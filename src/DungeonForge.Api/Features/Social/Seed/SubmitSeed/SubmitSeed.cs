using System.Security.Cryptography;
using System.Text;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Social.Seed.SubmitSeed;

public sealed record SubmitSeedRequest(string? PlayerName, string? Answer);

// Flavor tags only. How many teammates are still to answer is public (it is lobby progress), the axes never are.
public sealed record SubmitSeedResponse(string PlayerName, IReadOnlyList<string> Tags, bool Finalized, int Waiting);

public sealed class SubmitSeedHandler(
    SocialAccess access, IAxisParser parser, SocialLifecycle lifecycle, ChaosDispatcher chaos)
{
    public const string Operation = "Command:SubmitSeed";

    public async Task<Result<SubmitSeedResponse>> HandleAsync(Guid id, SubmitSeedRequest request, CancellationToken ct = default)
    {
        var resolved = access.Resolve(id, request.PlayerName, SocialModule.Seed, Operation);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var (party, member) = resolved.Value;
        var answer = NarrationText.Clean(request.Answer, SeedQuestions.MaxAnswerLength);

        string question;
        lock (party.SyncRoot)
        {
            if (party.Social().Profile(member.Name).Seeded)
            {
                return SocialErrors.SeedAlreadySubmitted(Operation, id, member.Name);
            }

            question = SeedQuestions.For(party.Social().RunSeed);
        }

        // Outside the lock: a model-backed parser can take seconds. The result is bounded in C# either way.
        var parse = (await parser.ParseAsync(question, answer, ct)).Sanitized();
        if (parse.IsLowEffort)
        {
            return SocialErrors.SeedLowEffort(Operation, id);
        }

        bool finalizedNow;
        SubmitSeedResponse response;
        lock (party.SyncRoot)
        {
            var social = party.Social();
            var profile = social.Profile(member.Name);
            if (profile.Seeded)
            {
                return SocialErrors.SeedAlreadySubmitted(Operation, id, member.Name);
            }

            // A copied answer (or an injection attempt) is accepted without effect: no feedback to learn from.
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(answer.ToLowerInvariant())));
            var copied = social.Profiles.Any(p => p != profile && p.SeedAnswerHash == hash);
            if (!copied && !parse.IsInjection)
            {
                foreach (var (axis, delta) in parse.Deltas)
                {
                    profile.Axes.Add(axis, delta);
                }

                profile.Tone.AddRange(parse.Tone);
            }

            profile.Seeded = true;
            profile.SeedAnswerHash = hash;

            finalizedNow = lifecycle.TryFinalize(party, force: false);
            if (social.Finalized)
            {
                lifecycle.EnsureAbilities(party);
            }

            var waiting = party.Members.Count(m => !social.Profile(m.Name).Seeded);
            response = new SubmitSeedResponse(member.Name, profile.Tags, social.Finalized, waiting);
        }

        access.Touch(party);
        if (finalizedNow)
        {
            await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.Narrator, "The dungeon has measured you all. Gifts stir in the dark."));
        }

        return response;
    }
}

public sealed class SubmitSeedEndpoint : IEndpoint
{
    private const int MaxNameLength = 40;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/social/seed", async Task<IResult> (
                Guid id, SubmitSeedRequest request, SubmitSeedHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(id, request, ct)).ToHttp(http))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.GetArgument<SubmitSeedRequest>(1);
               var errors = new Dictionary<string, string[]>();
               if (string.IsNullOrWhiteSpace(request?.PlayerName) || request.PlayerName.Trim().Length > MaxNameLength)
               {
                   errors["playerName"] = [$"Player name is required and at most {MaxNameLength} characters."];
               }

               var length = request?.Answer?.Trim().Length ?? 0;
               if (length < SeedQuestions.MinAnswerLength || length > SeedQuestions.MaxAnswerLength)
               {
                   errors["answer"] = [$"Answer must be {SeedQuestions.MinAnswerLength} to {SeedQuestions.MaxAnswerLength} characters."];
               }

               return errors.Count > 0
                   ? ValidationErrors.ToProblem(SubmitSeedHandler.Operation, errors, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("SubmitSeed");
}

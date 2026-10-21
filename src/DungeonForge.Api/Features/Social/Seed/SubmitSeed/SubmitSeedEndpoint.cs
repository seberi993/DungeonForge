using DungeonForge.Application.Features.Social.Seed;
using DungeonForge.Shared;
using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Social;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Seed.SubmitSeed;

namespace DungeonForge.Api.Features.Social.Seed.SubmitSeed;

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

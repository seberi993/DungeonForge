---
name: cqrs-slice
description: Add a Command or Query vertical slice to the Api, test first (Result<T>, IEndpoint, coded errors). Use when a new endpoint or feature handler is needed.
---

One slice = a feature folder in two projects, same path in both: the use case in `src/DungeonForge.Application/Features/<Group>/<FeatureName>/` and its HTTP endpoint in `src/DungeonForge.Api/Features/<Group>/<FeatureName>/`. No MediatR, no base classes. Work test-first.

## Files
Application (`DungeonForge.Application`, no ASP.NET):
- `Contracts.cs`: `sealed record` request and response.
- `Handler.cs`: `public sealed class <Name>Handler(...)` with `public const string Operation = "Command:<Name>"` (`Query:` for reads).
  A Command changes party state; a Query only reads.
- `Errors.cs` only when the slice has its own failures (see `add-error`). Shared ones live in `Application/Errors/PartyErrors.cs`.

Api (`DungeonForge.Api`):
- `<Name>Endpoint.cs`: `public sealed class <Name>Endpoint : IEndpoint`. `AddEndpoints` finds it by reflection; nothing to register.

## Steps
1. **Red.** HTTP test in `tests/DungeonForge.Tests/Features/<Group>/<Name>Tests.cs` with `WebApplicationFactory<Program>` as an
   `IClassFixture` (copy `UnlockTurnErrorTests.cs`). Happy path first, then each failure through `add-error`.
   Pure game math is not tested here: it belongs in `src/DungeonForge.Domain/Rules/` (see `add-game-rule`).
2. **Contracts.** Records only, nullable where the body may omit a field (validation runs in the filter).
3. **Handler.** Return `Result<TResponse>`. `Error` and `TResponse` both convert implicitly:
   ```csharp
   public sealed class UnlockTurnHandler(IPartyStore parties, IPartyNotifier notifier, ILogger<UnlockTurnHandler> logger)
   {
       public const string Operation = "Command:UnlockTurn";

       public async Task<Result<UnlockTurnResponse>> HandleAsync(Guid id, Guid memberId)
       {
           if (!parties.TryGet(id, out var party)) return PartyErrors.NotFound(Operation, id);
           lock (party.SyncRoot) { /* mutate through Party; one lock per party */ }
           parties.Save(party);
           await BestEffort.RunAsync(logger, Operation, id, "TurnLockUpdated", () => notifier.TurnLockUpdatedAsync(...));
           return new UnlockTurnResponse(...);
       }
   }
   ```
   - State comes from `IPartyStore` (`TryGet`, `Save`, `Touch`), never `IMemoryCache` directly. No database: Supabase is for scores only.
   - Take `party.SyncRoot` for every read-modify-write. Never `await` inside the lock.
   - SignalR pushes go through `IPartyNotifier` wrapped in `BestEffort.RunAsync`, so a dead socket never fails the request.
   - Game math and RNG are `DungeonForge.Domain/Rules/*` static classes taking `IRoller`; the handler only calls them.
4. **Endpoint.**
   ```csharp
   public sealed class UnlockTurnEndpoint : IEndpoint
   {
       public void MapEndpoint(IEndpointRouteBuilder app) =>
           app.MapPost("/parties/{id:guid}/unlock", async Task<IResult> (
                   Guid id, UnlockTurnRequest request, UnlockTurnHandler handler, HttpContext http) =>
                   (await handler.HandleAsync(id, request.MemberId)).ToHttp(http))
              .WithName("UnlockTurn");
   }
   ```
   Request validation goes in `.AddEndpointFilter(...)` and returns `ValidationErrors.ToProblem(Handler.Operation, errors, ctx.HttpContext)`.
   The route is relative to `/api`.
5. **Register the handler** in `Application/DependencyInjection.cs` (`services.AddSingleton<<Name>Handler>();`, next to the other handlers; social slices in `SocialRegistration.cs`).
6. **Green, then check.** `dotnet test --filter "FullyQualifiedName~<Name>"`, then `dotnet build` (zero warnings) and full `dotnet test`.
   `LayeringTests` fails if a project references one further out, or if Domain/Application name ASP.NET.
7. If an error was added, refresh `docs/errors.md` (see `add-error`). A new file or folder goes in `docs/map.md`.

## Limits
Keep files under ~250 lines. A handler that grows past that splits by concern into sibling classes in the slice
(see `Application/Features/Parties/ExploreDungeon/`: `GetScenario/`, `LockIn/`, `ResolveTurn/`, `TurnNarrator.cs`).

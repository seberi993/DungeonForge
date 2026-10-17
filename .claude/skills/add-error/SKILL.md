---
name: add-error
description: Add a new failure to an Api slice as a coded Error (code constant, ErrorSpec, factory, covering test). Use when a handler needs a new failure path, or an endpoint must answer a new 4xx.
---

Every handler failure is an `Error` returned in a `Result`; the endpoint maps it to ProblemDetails. Work test-first, one error at a time.

1. **Test first (red).** HTTP test in `tests/DungeonForge.Tests/Features/<Group>/`, marked `[CoversError(ErrorCodes.<Area>.<Reason>)]`:
   `using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.Conflict);` then assert `problem.Code`, `problem.Operation`,
   `problem.Parameter("partyId")`. A 400 that the Web client shows must also assert `errors.<field>`. Copy `UnlockTurnErrorTests.cs`.
2. **Code.** Add a `const string` to the nested class for its area in `src/DungeonForge.Api/Errors/ErrorCodes.cs`, as `<area>.<reason>`
   (lower case, underscores): `Vote.NotEligible = "vote.not_eligible"`.
3. **Spec.** One `static readonly ErrorSpec` per code, in `Features/<Group>/<Feature>/Errors.cs`; shared by several slices: in `Errors/PartyErrors.cs`.
   - `Kind`: `Validation` 400, `NotFound` 404, `Conflict` 409, `Unexpected` 500.
   - Pass a status only when it must differ from the kind (an old state conflict that is a 400): `ErrorKind.Conflict, status: 400`.
   - Pass `Field: "playerName"` when the response must carry `errors.playerName` (the Web client reads `errors` for every 400).
   - `Description` is one constant sentence. Never interpolate user input into it.
4. **Factory.** A method beside the spec that takes `operation` plus inputs and builds `Spec.Create(operation, Param.PartyId(id), ...)`.
   `Param` is the allow-list (`partyId`, `memberId`, `turn`, `floor`, `playerName`, `room`, `locked`, `required`, `code`). Add a key there
   only for a request echo or public game state. Never secrets: traitor identity, pocket contents, whispers, keys, tokens.
5. **Handler (green).** The handler has `public const string Operation = "Command:<Name>"` (or `Query:`) and returns `Result<T>`; raise the
   error with `return XErrors.Thing(Operation, id, ...);`. The endpoint stays `.ToHttp(http)`. Endpoint filters return
   `ValidationErrors.ToProblem(Handler.Operation, errors, ctx.HttpContext)`.
6. **Refresh the docs.** `UPDATE_ERRORS_DOC=1 dotnet test --filter ErrorsDocTests`, then commit the changed `docs/errors.md`.
7. **Check.** `dotnet build` (zero warnings) and `dotnet test`. `ErrorCatalogTests` fails if the code has no spec, no `[CoversError]` test or a
   malformed name.

Not errors: the waiting 409 (`WaitingForPartyResponse`, a success variant) and domain success outcomes such as a hidden pocket. Domain methods
keep returning their own results; the handler translates them.

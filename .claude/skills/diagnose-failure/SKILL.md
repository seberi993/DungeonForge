---
name: diagnose-failure
description: Trace an Api failure (ProblemDetails response, coded log line or failing test) to its raising code and covering test with the narrowest test run. Use when a request fails, a 4xx/5xx body or log event is reported, or an error test breaks.
---

Start from whichever of the three you have, get to a **code** or **event**, then walk the same path.

**From a response body** (`application/problem+json`): read `code`, `operation`, `parameters`, `traceId`. A 400 also has `errors`.
**From a log line:** read `EventId`/`EventName` and the scope values `Operation`, `PartyId`, `Turn`. `Turn` is the number the matching
message carries (a resolve answers with the turn after it; narrator events carry the turn narrated).
**From a failing test:** the test name or its `[CoversError(...)]` marker is the code.

1. **Find the definition.** `grep -rn "<code>" src docs/errors.md`. Hits: the `ErrorCodes` constant, its `ErrorSpec` (kind, status, description),
   its factory method, and the row in `docs/errors.md` ("Defined in" file and "Covered by" test).
   For a log event: `grep -n "<EventName>" src/DungeonForge.Api/Errors/LogEvents.cs`, then grep `logger.<EventName>(` for the call site.
2. **Find the raiser.** The factory is called from a handler. The `operation` names it (`Command:ResolveTurn` is under
   `Features/Parties/ExploreDungeon/`; `docs/errors.md` "Operations" lists each operation's folder). Open that `Handler.cs` and grep the
   factory name.
3. **Reproduce narrowly.** Run the covering test alone, not the suite:
   `dotnet test tests/DungeonForge.Tests --filter "FullyQualifiedName~<TestClass>.<TestName>"`.
   For a whole slice: `--filter "FullyQualifiedName~<Feature>"`. Web client behaviour: `tests/DungeonForge.Web.Tests`,
   `--filter "FullyQualifiedName~DungeonApiClient"`.
4. **Fix test-first.** Change or add the covering test, see it fail, fix the handler, rerun the narrow filter, then the full `dotnet test`.

Special cases
- `unexpected.unhandled` (500): `operation` is `Unhandled:<endpoint name>`. The exception is in the Error log entry
  (event 1001, declared in `Errors/PipelineExceptionHandler.cs`), never in the response. Find it by `Operation`/time, then fix the thrower.
- `request.malformed` (400): the body is missing or does not bind (bad JSON, unknown enum value). Operation is `BindRequest:<endpoint name>`.
  Event 1002 (same file), a Warning.
- `validation.invalid_request` (400): an endpoint filter rejected the body; `errors` names the fields; operation is the handler's.
- A 409 with `status: "waiting"` and no `code` is not an error: not every member has locked in.
- Broadcast, narrator, leaderboard and hub problems never fail the request. They are log events only (`BroadcastFailed`, `Narrator*`,
  `KeyRateLimited`, `ScoreBoardReadFailed`, `Hub*`); the response was a success.
- Logs and parameters never contain keys, response bodies, exception text from the narrator, or a typed player name in hub events. If one
  does, that is the bug.

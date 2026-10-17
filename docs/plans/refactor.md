# DungeonForge refactor plan


## Context

Goal: fewer agent tokens per task. That means predictable locations, small single-purpose files, greppable error codes and tests that read as the spec.

Phase 1 adds a shared `Result`/`Error`/ProblemDetails layer. Phase 2 is a structure-only refactor. Behaviour stays unchanged apart from the error bodies listed in "Intentional behaviour changes".

### Reality checks against the brief

| Brief assumed | Found | Consequence |
|---|---|---|
| `docs/plans/_template.md` | `docs/` does not exist (`git ls-files`) | The format below is the template. It is derived from the requested step fields. |
| `docs/map.md` (Phase 2 step 9) | Does not exist | Phase 2 creates it. It is not "updated". |
| `bd` CLI | Not on PATH (`which bd` fails) | Steps are written as `bd create` field sets. They have not been executed. |
| Coverage run first | Plan mode forbids running tests | Each phase's first step is the coverage run. The command is below. Coverage numbers are **not** in this plan. Gap candidates come from grepping test names. |
| "~27 catch blocks" | Confirmed for Api: grep `catch\b` gives 27 in 10 files, 16 of them in `ExploreDungeon/Handler.cs`. Web has 10 more. | The count includes any comment hits. The Web catches are not in scope for coded logging, because they run in the browser. |

Coverage command, run from the repo root, once per phase on the touched assemblies:
`dotnet test --collect "XPlat Code Coverage" --results-directory <scratchpad>`. `TestResults/` is already gitignored (`.gitignore`).

Zero warnings is enforced by the build itself: `Directory.Build.props:5` sets `TreatWarningsAsErrors=true`.

---

# Phase 1: Result, Error and ProblemDetails

## Verified findings (file:line)

- **8 status enums, no shared error type.**
  - `ExploreStatus`: `ExploreDungeon/Contracts.cs:142`
  - `ClientNarrationStatus`: `ExploreDungeon/Contracts.cs:135`
  - `JoinPartyStatus`: `JoinParty/Contracts.cs:9`
  - `ClaimVaultStatus`: `ClaimVault/Contracts.cs:11`
  - `CastVoteStatus`: `CastConfrontationVote/Contracts.cs:10`
  - `ExtractStatus`: `ExtractRun/Contracts.cs:8`
  - Domain `VaultClaimStatus` and `VoteStatus`: `Domain/ChaosTypes.cs:22,52`
- **ProblemDetails exists only through `TypedResults.ValidationProblem`.** It appears in 7 files: `CreateParty/Endpoint.cs:53`, `JoinParty/Endpoint.cs:39`, `ClaimVault/Endpoint.cs:17,35`, `CastConfrontationVote/Endpoint.cs:17,35`, `ExtractRun/Endpoint.cs:17,25`, `ExploreDungeon/Endpoint.cs:17,23`, `SubmitNarration/Endpoint.cs:15`. Each builds its own dictionary.
  - `Program.cs` has no `AddProblemDetails` and no `UseExceptionHandler`. The pipeline is just `UseCors`, `MapEndpoints` and `MapHub` (`Program.cs:103-110`).
- **Bare-body failures.** 404s are bare (`GetPartyState/Endpoint.cs:12`, `JoinByCode/Endpoint.cs:12`, `UnlockTurn/Endpoint.cs:13`). Defeated is a bare 409 (`ExploreDungeon/Endpoint.cs:15`).
- **Waiting 409 is a success variant.** Its body is `WaitingForPartyResponse` with `Status = "waiting"` (`ExploreDungeon/Contracts.cs:159-164`, `Endpoint.cs:16`).
- **Web backward compatibility confirmed.** `DungeonApiClient.IsWaitingBodyAsync` requires `status.ValueKind == String && == "waiting"` (`DungeonApiClient.cs:97-100`). A ProblemDetails `status` is a number, and an empty body hits `JsonException`, so both return false. A ProblemDetails 409 therefore already reads as Conflict/defeated.
- **Trap found: client 400 message.** For a 400 the client reads only `errors` (`DungeonApiClient.cs:108-131`). A plain ProblemDetails without an `errors` dictionary would silently degrade to the fallback text "The request was invalid.". So every 400, including status-overridden ones, must keep an `errors` entry (see design, `ErrorSpec.Field`).
- **Four explore failures are 400 today although they are state conflicts.** All go through `Invalid(...)` (`Handler.cs:671`), which the endpoint maps to ValidationProblem (`Endpoint.cs:17`):
  - empty actions (`Handler.cs:167`)
  - confrontation in progress (`:549`)
  - no active encounter / scenario still being written (`:554-556`)
  - no non-empty discussion (`:563`)
- **Handler shapes today.**
  - Query/Command mix: `CreatePartyHandler.Handle` returns the response directly (`CreateParty/Handler.cs:11`).
  - `GetPartyState`, `JoinByCode` and `UnlockTurn` return null for 404.
  - `GetHighScoresHandler` swallows `HttpRequestException` and `TaskCanceledException` (`GetHighScores/Handler.cs:17`).
  - `SubmitNarration` has no handler of its own. It calls `ExploreDungeonHandler.SubmitClientNarrationAsync` (`SubmitNarration/Endpoint.cs:21`, `Handler.cs:463`).
- **Hub failures are `HubException`s with client-visible text.** `DungeonHub.cs:15,26,30` throws "Party not found." and "Player not found.". That text is a SignalR contract, so it stays (see step 1.5c).
- **Best-effort broadcast copies.**
  - `ExploreDungeon/Handler.cs`: `:103`, `:320`, `:507`, `:745`, `:754`
  - `CastConfrontationVote/Handler.cs:64`
  - `ClaimVault/Handler.cs:70`
  - `JoinParty/Handler.cs:38`
  - `UnlockTurn/Handler.cs:32`
  - Total: 9 call sites in 5 files. The brief said ~6 copies.

### Status-code table (current, to be frozen by step 1.1)

| Route | Failure | Today | Source |
|---|---|---|---|
| POST /parties | validation | 400 VP | `CreateParty/Endpoint.cs:52` |
| POST /parties/by-code/{code}/join | validation | 400 VP | `JoinParty/Endpoint.cs:38` |
| | unknown/malformed code or party | 404 bare | `JoinParty/Handler.cs:17` |
| | duplicate name or full | 409 bare | `JoinParty/Handler.cs:27` |
| GET /parties/by-code/{code} | not found | 404 bare | `JoinByCode/Endpoint.cs:12` |
| GET /parties/{id} | not found | 404 bare | `GetPartyState/Endpoint.cs:12` |
| POST /parties/{id}/unlock | not found | 404 bare | `UnlockTurn/Endpoint.cs:13` |
| | unknown member | 200 (idempotent, not an error) | `UnlockTurn/Handler.cs:38` |
| POST /parties/{id}/explore | actions fail limits | 400 VP | `Endpoint.cs:22` |
| | not found | 404 bare | `Handler.cs:85,113,535` |
| | party defeated | 409 bare | `Handler.cs:120,543` |
| | waiting for members | 409 + `WaitingForPartyResponse` (success variant, untouched) | `Endpoint.cs:16` |
| | 4 state-conflict `Invalid` cases | 400 VP (`errors.actions`) | `Handler.cs:167,549,554,563` |
| POST /parties/{id}/narration | text too long | 400 VP | `SubmitNarration/Endpoint.cs:13` |
| | not found | 404 bare | `Handler.cs:467` |
| | no job / turn claimed | 409 bare | `Handler.cs:473,489,495` |
| POST /parties/{id}/vault/claim | validation | 400 VP | `ClaimVault/Endpoint.cs:35` |
| | not found | 404 bare | `ClaimVault/Handler.cs:18` |
| | no vault, confrontation active | 409 bare | `Endpoint.cs:16` |
| | unknown player | 400 VP | `Endpoint.cs:17` |
| POST /parties/{id}/confrontation/votes | validation | 400 VP | `CastConfrontationVote/Endpoint.cs:35` |
| | not found | 404 bare | `Handler.cs:17` |
| | no confrontation | 409 bare | `Endpoint.cs:16` |
| | not eligible | 400 VP | `Endpoint.cs:17` |
| POST /parties/{id}/extract | validation | 400 VP | `ExtractRun/Endpoint.cs:25` |
| | not found | 404 bare | `Handler.cs:16` |
| | no pact | 409 bare | `Endpoint.cs:16` |
| | unknown player | 400 VP | `Endpoint.cs:17` |
| GET /scores, GET /classes/{class}/spells | none (scores swallows outages and returns empty) | n/a | `GetHighScores/Handler.cs:17` |

## Target design (refinements to the brief, flagged)

- **Location.** New `src/DungeonForge.Api/Errors/`, outside `Features` and `Domain`, so Phase 2's architecture test stays simple.
  - `Error.cs`
  - `Result.cs`
  - `ErrorKind.cs`
  - `ErrorCodes.cs`: nested static classes of `const string` (`ErrorCodes.Turn.PartyDefeated = "turn.party_defeated"`)
  - `ErrorSpec.cs`
  - `ErrorMapping.cs`
  - `ErrorCatalog.cs`
- Each slice owns its factories in `Features/<G>/<F>/Errors.cs`. Shared ones (party not found, unknown player) live in `Errors/SharedErrors.cs`.
- **`ErrorSpec` (addition to the brief).** One static readonly per code. It holds `Code`, `Kind`, an optional status override, a constant one-sentence `Description` and an optional `Field`.
  - It is the single definition of each message.
  - It carries the per-error status override (`Kind.Conflict` with status 400).
  - Setting `Field` makes the mapper emit `errors:{field:[description]}`. That keeps the 400 body the Web client reads today.
  - The catalog reflects over the specs, which makes `docs/errors.md` cheap to generate.
- **`Error`.** `Code`, `Kind` (Validation/NotFound/Conflict/Unexpected), `Operation`, `Description`, `Parameters` (`IReadOnlyDictionary<string,string>`).
  - `Operation` is a `public const string` on each handler, for example `ResolveTurnHandler.Operation = "Command:ResolveTurn"`. Factories receive it; call sites never type it.
  - ExploreDungeon keeps three constants chosen in `HandleAsync` (`Handler.cs:40-45`): `Query:GetScenario`, `Command:LockIn`, `Command:ResolveTurn`. Phase 2's split then changes no bodies.
- **Rule: a handler with no failure path may return its value directly.** `CreateParty` and `GetHighScores` do this. Document it in the `cqrs-slice` skill in Phase 2 step 9, and for now in `docs/errors.md`.
- **Mapper.** `ErrorMapping.ToProblem(Error, HttpContext)` returns an `IResult`.
  - Status comes from `Kind` (404/409/400/500) unless `ErrorSpec` overrides it.
  - Extensions are `code`, `operation`, `parameters` and `traceId`. Validation keeps `errors`.
  - The Waiting 409 never goes through it.
  - Endpoint filters (the 7 hand-built validators) call one `ValidationErrors` helper that returns the same mapper output.
  - Endpoint return types become `Task<IResult>`. There is no OpenAPI package, so there is no typed-union loss.
- **Parameters allow-list.** A reflection test enforces that parameter keys come from `partyId, turn, floor, memberId, playerName (≤40 chars), room, locked, required, code`.
  - Never traitor identity, pocket state, secrets, gaslighting content, keys or tokens.
- **Outside a request.** `LoggerMessage` source-generated, one `EventId` range per area, inside `BeginScope` with `Operation`, `PartyId` and `Turn`. One `BestEffort.RunAsync(logEvent, ...)` helper replaces the 9 broadcast copies.
- **Traceability.** `[CoversError(ErrorCodes.X)]` takes `[CallerFilePath]`, so the docs generator gets the test file with no manual column. One reflection test fails when a code has no covering test.

## Steps (each = one `bd create`, children of epic "Phase 1: Result/Error/ProblemDetails")

Shared **slice recipe** for steps 1.4a-g, written out once for token economy:
1. Characterization test of the current status codes already exists from 1.1.
2. Red: new tests assert body `code`, `operation`, `parameters` and `traceId` plus a `[CoversError]` marker.
3. Green: add `Errors.cs` factories, add the `Operation` const, and make the handler return `Result<T>`.
4. Replace the endpoint's `switch` with `result.Match(Ok, ErrorMapping.ToProblem)`.
5. Delete the slice's status enum.
6. The domain method is untouched.
7. Build with zero warnings and run `dotnet test --filter "FullyQualifiedName~<Slice>"`, then the full run.

### 1.1 Baseline coverage, status table, and tests for uncovered failure paths
- type `task`, priority P1, depends on: none, size M
- Files: `tests/DungeonForge.Tests/Features/Parties/FailureStatusTests.cs` (new). The table lives in `docs/errors.md` as a stub.
- Tests first: run coverage, then add one status-code characterization test per row above that has no endpoint test.
  - Covered today by grepping tests: Explore 404 (`ExploreDungeonTests.cs:104,112`), Explore defeated 409 (`:306`), Explore waiting 409 (`TurnLockTests.cs:147`), unlock 404 (`TurnLockTests.cs:252`), join 404 and 409 (`JoinPartyTests.cs:36,47`), get-state 404 (`GetPartyStateTests.cs:39`), by-code 404 (`JoinByCodeTests.cs:55`), CreateParty 400 (`CreatePartyTests.cs:165,183`), claim/vote rejections (`VaultAndConfrontationTests.cs:174,238` — verify that these go through HTTP and not only the domain), extract with no pact (`TraitorPactTests.cs:201`).
  - Suspected gaps to confirm with coverage: the 4 explore `Invalid` 400s, the narration 400/404/409, the claim `NoVault` and `ConfrontationActive` 409s, the vote 409, and the extract unknown-player 400.
- Token benefit: it creates a frozen contract, so later steps can be changed mechanically without rereading handlers.
- Acceptance: the table is in `docs/errors.md`, and every row has a passing test asserting status only. Zero warnings and green.

### 1.2 Core: `Error`, `Result`, codes, specs, mapper, `[CoversError]` + reflection test
- type `task`, P1, depends on 1.1, size M
- Files: new `Errors/*`, `tests/DungeonForge.Tests/Errors/` (`ErrorMappingTests`, `ErrorCatalogTests`).
- Tests first (red→green→refactor):
  - Kind→status table.
  - Status override.
  - `Field` yields `errors`.
  - Extensions are present.
  - `Parameters` allow-list.
  - Reflection: every `ErrorCodes` const has a spec and ≥1 `[CoversError]`.
  - Code format `<area>.<reason>` and uniqueness.
- Token benefit: one definition per error, so an agent can grep a code and land on its spec, factory and test.
- Acceptance: nothing is wired to endpoints yet. Zero warnings and green.

### 1.3 `AddProblemDetails` + exception handler
- type `task`, P1, depends on 1.2, size S
- Files: `Program.cs:101-110`, `Errors/UnexpectedExceptionHandler.cs`.
- Tests first: a factory test that replaces `IMemoryCache` with a throwing stub and calls GET /parties/{id}. Expect a 500 `application/problem+json` with `code=unexpected.unhandled`, a `traceId`, no stack trace, and one error log.
  - **Unknown to verify by test:** whether malformed JSON bodies (`BadHttpRequestException`) become ProblemDetails or keep today's body. Today's behaviour is pinned first and the result is written into the changes list.
  - Do not add `UseStatusCodePages`, so route-miss 404s stay as they are.
- Token benefit: it removes the need to explain unhandled errors in every handler.
- Acceptance: the exception path is covered. Existing endpoints are unchanged.

### 1.4a UnlockTurn → Result (query-like, 404 only)
- type `task`, P2, depends on 1.3, size S
- Files: `Features/Parties/UnlockTurn/{Handler,Endpoint,Errors}.cs`, `Errors/SharedErrors.cs` (adds `PartyErrors.NotFound`).
- Tests first: `TurnLockTests.cs:252` plus a body assertion.
- Token benefit: it is the template slice; later slices copy it.
- Acceptance: the slice recipe holds, and the 404 body has `code=party.not_found` with `parameters.partyId`.

### 1.4b GetPartyState → Result
- P2, depends on 1.4a, size S
- Files: `GetPartyState/{Handler,Endpoint}.cs`. Tests first: `GetPartyStateTests.cs:39`.

### 1.4c JoinByCode → Result
- P2, depends on 1.4a, size S
- Files: `JoinByCode/{Handler,Endpoint}.cs`. Tests first: `JoinByCodeTests.cs:55`. Note that the unknown code and malformed code cases share one error. `parameters.code` is an invite code and is short.

### 1.4d JoinParty → Result
- P2, depends on 1.4a, size S
- Files: `JoinParty/{Contracts,Handler,Endpoint,Errors}.cs`. Tests first: `JoinPartyTests.cs:36,47`, plus a new case for a full party (`Party.MaxMembers`, `Party.cs:79`).
- The filter validation moves onto the shared `ValidationErrors` helper.

### 1.4e ExtractRun → Result
- P2, depends on 1.4a, size S
- Files: `ExtractRun/{Contracts,Handler,Endpoint,Errors}.cs`. Tests first: `TraitorPactTests.cs:201` plus the 1.1 tests.
- Domain `TryExtract` stays (Phase 1 leaves the domain as is).

### 1.4f ClaimVault → Result
- P2, depends on 1.4a, size S
- Files: `ClaimVault/*`. The handler translates `VaultClaimStatus` into `Error`s.
- Domain success outcomes (`Shared`, `PocketedHidden`, `PocketedCaught`) are not errors and stay (`Handler.cs:38-57`).

### 1.4g CastConfrontationVote → Result
- P2, depends on 1.4a, size S
- Files: `CastConfrontationVote/*`. The same translation as 1.4f for `VoteStatus`.

### 1.4h SubmitNarration → Result (error surface only)
- P2, depends on 1.4a, size S
- Files: `SubmitNarration/Endpoint.cs`, the `SubmitClientNarrationAsync` signature in `ExploreDungeon/Handler.cs:463`.
- The method returns `Result` instead of `ClientNarrationStatus`. The success path is still 204. Phase 2 moves the method out.
- Tests first: `ClientNarrationTests.cs` (7 status asserts) plus a body assertion.

### 1.4i ExploreDungeon → Result (last, and only its error surface)
- P1, depends on 1.4a-h, size L
- Files: `ExploreDungeon/{Contracts,Handler,Endpoint,Errors}.cs`.
- `ExploreResult` becomes `Result<ExploreOutcome>`. Waiting and `LockIn` both stay in `ExploreOutcome`, so the waiting 409 body is untouched.
- The 4 `Invalid(...)` sites become specs with kind `Conflict` and a 400 override, with `Field="actions"`.
- Tests first: the 1.1 tests, `TurnLockTests`, `ExploreDungeon*Tests`. Run the whole suite, because the RNG call order must not move.
- Token benefit: it removes `ExploreStatus`, `Invalid(...)` and the 5-way `switch` in the endpoint.
- Acceptance: every response is byte-equivalent apart from the new fields. The scripted-roller tests are green.

### 1.5a `BestEffort` helper + coded events in the small handlers
- type `task`, P2, depends on 1.4g, size M
- Files: `Errors/BestEffort.cs`, `Errors/LogEvents.cs`; the handlers `CastConfrontationVote`, `ClaimVault`, `JoinParty`, `UnlockTurn`, `ChaosDispatcher.cs:126`, `GetHighScores:17`.
- Tests first: a capturing `ILogger` asserts `EventId`, scope values and that a failing notifier does not fail the request. This shows whether an existing log-capture helper can be reused.
- Token benefit: 4 broadcast copies are collapsed into one.
- Acceptance: no free-text `LogWarning` remains in those files.

### 1.5b Coded events in ExploreDungeon handler (16 catch sites)
- P2, depends on 1.4i and 1.5a, size M
- Files: `ExploreDungeon/Handler.cs` (the lines listed above).
- The sites are replaced mechanically. The narrator is called exactly once per turn, and the existing tests (`ExploreDungeonNarrationTests`) are the guard.
- Acceptance: `grep -c "LogWarning(ex" Handler.cs` is 0.

### 1.5c Coded events in narration infrastructure and the hub
- P3, depends on 1.5a, size S
- Files: `GeminiNarratorService.cs`, `KeyPoolGeminiNarrator.cs`, `TurnNarrationParser.cs`, `RateLimitInfo.cs`, `DungeonHub.cs`.
- The `HubException` text is left unchanged because it is a client contract. A coded event is logged next to each throw.

### 1.6 Web: `ApiResult<T>` carries `code`, `description`, `parameters`
- type `task`, P2, depends on 1.4i, size M
- Files: `Web/Models/ApiResult.cs`, `Web/Services/DungeonApiClient.cs:60-131`, `tests/DungeonForge.Web.Tests/DungeonApiClientTests.cs:202`, `DungeonApiClientLockTests.cs`; callers: `DungeonRoom.razor` (2), `PartyLobby.razor` (4), `SpellbookDrawer.razor` (1).
- Tests first, in the same step: the waiting body is still parsed; a ProblemDetails 409 with `code=turn.party_defeated` gives Conflict; a ProblemDetails 400 shows `description`; an old body without `code` falls back to today's logic.
- Token benefit: the client stops guessing from status plus body shape.
- Acceptance: `ApiStatus` is unchanged. The waiting decision uses the code when present.

### 1.7a `docs/errors.md` generated from the catalog
- type `chore`, P3, depends on 1.4i and 1.5b, size S
- Files: `tests/.../Errors/ErrorsDocTests.cs` (golden-file test: it fails if the file is stale, and `UPDATE_ERRORS_DOC=1` rewrites it), `docs/errors.md`.
- Columns: code, kind, status, operation, covering test (from `[CallerFilePath]`). The raising file is the slice's `Errors.cs`, derived from the namespace.
- Acceptance: the file is generated, and a stale file fails the test.

### 1.7b Skills `add-error` and `diagnose-failure`
- type `chore`, P3, depends on 1.7a, size S
- Files: `.claude/skills/add-error/SKILL.md`, `.claude/skills/diagnose-failure/SKILL.md`. Use the `test-writer` agent for the skeleton.
- The diagnose path is: ProblemDetails/log/failing test → code → `ErrorCodes` → spec → factory → raising file → `[CoversError]` test → the narrowest `--filter`.

**Done when:** every handler failure path returns an `Error`; every Api failure outside a request logs a coded event; every non-2xx response except the waiting 409 is ProblemDetails with `code`, `operation`, `parameters` and `traceId` (route-miss and framework-level 404s excepted, see 1.3); the status codes match the 1.1 table except the listed changes; the reflection test passes; green with zero warnings.

---

# Phase 2: Structure, readability, decoupling

Rule for every step: run coverage first (Phase 2 step 2.0). Add characterization tests only for uncovered paths. The `architect-reviewer` agent reviews each step before it is committed. No behaviour change.

## Verified findings

- **Rule locations**
  - `FailDamage`, `Gold`, `WoundedRatio`: `ExploreDungeon/Handler.cs:30,676,685,693`. They are private, so only the full endpoint tests reach them.
  - `RoomKinds = 3`: `Handler.cs:31,133,630`. `RoomType` has `Boss` as a value, and `(RoomType)roller.Next(3)` never yields it. This is implicit today.
  - `MaxMembers = 8` is duplicated: `Domain/Party.cs:24` and `CreateParty/Endpoint.cs:9`.
  - Other rule homes: `ActionOdds.cs`, `Party.cs`, `PartyMember.cs`, `ChaosTypes.cs`, `CovertResolver.cs`, `RoomSecrets.cs`, `Gaslighting.cs` (these three are in `Features/Parties/Chaos/`).
  - `MaxManaFor`: `Web/Services/GameSession.cs:60`. It is client-only.
- **Layering.** 4 Domain files import `Features.Parties.ExploreDungeon`: `PendingEncounter.cs:1`, `Party.cs:1`, `Party.Chaos.cs:1`, `ChaosTypes.cs:1`. They need `RoomType` (`Contracts.cs:7`) and `IRoller` (`IRoller.cs:3`).
  - 6 Infrastructure/Narration files import `ExploreDungeon` too. `NarrationPrompts` and `MasterPromptBuilder` use `RoomType`; the rest also need `NarrationContext` etc., which already live in Infrastructure (`IDungeonNarrator.cs`). Moving `RoomType` and `IRoller` clears most of them.
  - `SupabaseScoreBoard` imports `Features.Scores` (a port; leave it).
- **Cache lookup copies.** `PartyCache.Key(` appears 30 times in 10 files (`ExploreDungeon/Handler.cs` has 15, `DungeonHub.cs` 2).
- **Dead members** (all in `Domain/Party.cs`): `ClearLocks:288`, `ActionBonusCount:341`, `ApplyChoiceOutcome:354`, `TrySetPending:152`, `TakeDamage(int, ElementType):107`.
  - Production callers: none. The only `src` caller of `TakeDamage` is the dead `ApplyChoiceOutcome` (`:358`).
  - **Caveat the brief missed:** tests use these as setup, not only to test them. `TrySetPending` is used in `PartyLockTests.cs:18`, `PartyActionTests.cs` (6 sites) and `PartyPendingEncounterTests.cs`. `party.TakeDamage(...)` appears in 20+ test lines in `PartyTests.cs`, in `PartyFloorTests.cs:85,99` and in the Chaos tests (`VaultAndConfrontationTests.cs:302`, `ChaosHardeningTests.cs:130`).
  - So deletion needs setup helpers first.
  - Deleting `TakeDamage(int, ElementType)` also strands the threat-targeting logic. Only `Party.cs:117` uses `.Threat` for targeting, and that is the dead method. 11 hits of `Threat|IsOut|IsBanished` across `Party.cs`, `Party.Chaos.cs` and the handler.
- **Mixed name comparisons.**
  - Ordinal `==`: `Party.cs:131,315,375`, `Handler.cs:667`.
  - Case-insensitive: `ClaimVault/Handler.cs:34`, `CreateParty/Endpoint.cs:47`.
  - These are listed, not unified.
- **Magic strings.**
  - `"server"`, `"fallback"`: `Handler.cs:274,279,285,309,493`
  - `"model"`: `Handler.cs:493`
  - `"scenario"`, `"turn"`: `Handler.cs:396,406`
  - `"Adventurer"`: `Handler.cs:217,524` and `GameSession.cs:15` (3 copies)
  - `DungeonRoom.razor:99` compares `NarrationSource == "fallback"`.
- **Limit duplication, with a latent bug.** `PartyLobby.razor:18` allows `maxlength=50` for a party name, but the server rejects more than 40 (`CreateParty/Endpoint.cs:7`). Member names are 30 on the client against 32 on the server (`PartyLobby.razor:24,61`; `Endpoint.cs:8`).
  - `MaxActionTextLength = 200` is defined twice: `ExploreDungeon/Contracts.cs:18` and `MasterPromptBuilder.cs:10`. The client uses a literal 200 (`DungeonRoom.razor:130`).
  - `StageDirector.cs:45` hard-codes 8 for max heroes.
- **File sizes (over ~250 lines in `src`).**
  - `ExploreDungeon/Handler.cs` 762
  - `StageDirector.cs` 673
  - `DungeonRoom.razor` 631
  - `Domain/Party.cs` 434
  - `GameSession.cs` 424
  - `Party.Chaos.cs` 273
  - Tests over 250: 11 files, of which the brief covers 2 (see Suggested).
- **Names and Handler shape.**
  - `Mutate` returns a 4-tuple (`Handler.cs:529`).
  - `BeginPrompt` returns a tuple (`:109`).
  - `PromptCoreAsync` (`:58`).
  - `ExploreResponse` has 24 constructor parameters.
  - `waitingIsConflict` is a boolean flag parameter (`:163`).
  - Nullable `= null` dependencies (`:25-28`).
  - `_jobs` plus `ScheduleDeadline` (`:36,411`) live in the handler.
- **Stage timings.** `StageDirector.cs:12-15` already name 4 constants. Other numerics remain (a regex count shows 16 numeric literals). The constant cleanup is smaller than the brief implied.
- **Agent docs.** `cqrs-slice/SKILL.md` documents a static `Map…Endpoint` and a nonexistent `DungeonDbContext`. The real shape is `IEndpoint` classes (`Endpoints/IEndpoint.cs`, `EndpointExtensions.cs:8-27`).

## Steps (children of epic "Phase 2: Structure/readability/decoupling")

### 2.0 Coverage baseline + characterization tests for rule code
- type `task`, P1, depends on Phase 1 done, size M
- Files: `tests/.../Domain/Rules/*` (new).
- Tests first: the coverage run, then characterize the **uncovered** private math (`FailDamage`, `Gold`, `IsWounded`) through the handler. Table-driven expectations come from the current formulas (`Handler.cs:676-694`).
- Token benefit: the later moves can be verified by a test that fails without the code, instead of by re-reading.

### 2.1 Move `RoomType`, `IRoller` (+ `RandomRoller`) to Domain
- type `chore`, P1, depends on 2.0, size S
- Files: `Domain/RoomType.cs`, `Domain/IRoller.cs`, 4 Domain files, 6 Narration files, handlers, tests' usings.
- Tests first: the architecture test (below) is written first and fails.
- Acceptance: namespaces change only. The JSON enum names are unchanged (camelCase converter, `Program.cs:30`).

### 2.2 Architecture test: Domain never references Features or Infrastructure
- type `task`, P1, depends on 2.1, size S
- Files: `tests/DungeonForge.Tests/Architecture/LayeringTests.cs`.
- No new package (justification: `NetArchTest` and `ArchUnitNET` cost a dependency for a one-line rule). The test walks `src/DungeonForge.Api/Domain/**/*.cs` and fails on any `DungeonForge.Api.Features` or `DungeonForge.Api.Infrastructure` text. Reflection cannot see `using`s or method bodies.
- It also asserts that `Errors/` references neither `Features` nor `Domain`.

### 2.3 Rules to `Domain/Rules/` with one test file per rules file
- type `task`, P1, depends on 2.2, size L, as multiple commits
- Files, with an `internal static` class per file:
  - `ActionOdds` (moved from `ExploreDungeon/ActionOdds.cs`)
  - `RoomRules` (`FailDamage`, `Gold`, `WoundedRatio`, `RoomKinds`)
  - `PartyRules` (boss HP, rooms per floor, floor heal, `MaxMembers`; `CreateParty/Endpoint.cs:9` then reads it)
  - `MemberRules` (HP, threat)
  - `ChaosRules` (confrontation, perception, traitor constants from `ChaosTypes.cs`)
  - `CovertResolver`, `RoomSecrets`, `Gaslighting` (from `Features/Parties/Chaos/`)
- Tests: `tests/.../Domain/Rules/<Name>Tests.cs`, one per file. The moves are a 1:1 rename of calls.
- The RNG call order is not touched.
- **"Never Boss" becomes explicit:** `RoomRules.RollableRoomKinds` (an array of the 3 non-boss rooms) replaces the `RoomKinds = 3` cast (`Handler.cs:133,630`). The test pins that the roller's bound is still 3.
- Token benefit: "where is the damage formula?" has a one-folder answer.

### 2.4 `IPartyStore` (`TryGet`, `Save`, `Touch`)
- type `task`, P2, depends on 2.2, size M
- Files: `Features/Parties/PartyStore.cs` and `IPartyStore.cs` (replacing `PartyCache.cs`, whose key and entry options become private to the store), 10 handlers/hub, `Program.cs`.
- Tests first: `PartyStoreTests` (round trip, `Touch` keeps the sliding expiry, the cache `Size=1` requirement from `Program.cs:22`).
- Do each handler mechanically, running `--filter` per slice.
- `ExploreDungeon/Handler.cs` is excluded and moved in 2.5, to avoid rewriting it twice.

### 2.5 Split `ExploreDungeon/Handler.cs`
- type `task`, P1, depends on 2.3 and 2.4, size L, split into sub-commits
- Target files, each ≲250 lines, all inside `Features/Parties/ExploreDungeon/`:
  - `GetScenario/` (Query): `BeginPrompt`, `PromptResponse`, `AwaitingResponse` → named record `ScenarioClaim` replaces the `(Early, Context)` tuple
  - `LockIn/` and `ResolveTurn/` (Commands): `Mutate` → renamed and returning a named `TurnResolution` record instead of the 4-tuple; the `waitingIsConflict` bool becomes two entry points
  - `ClientNarrationCoordinator.cs`: `_jobs`, `ScheduleDeadline`, `RunServerNarrationAsync`, `SubmitClientNarrationAsync` (and `SubmitNarration` calls it directly)
  - `TurnNarrator.cs`: `NarrateAndNotifyAsync`, `FinishTurnAsync`, the safe-narration wrappers
  - null-object defaults replace `= null` for `options`, `scores`, `chaos` and `vocabulary` (`Handler.cs:25-28`)
- The endpoint stays one route. The three handlers sit behind it (see decision 3). The Phase 1 operation constants map one-to-one onto the new handlers.
- The one rule that must hold: **one narrator call per turn**, and the lock is taken at the same points. `ExploreDungeonNarrationTests`, `TurnLockTests` and the `ClientNarrationTests` race are the guard. Run them after every sub-commit.

### 2.6 Split `Party` into internal parts, keep one lock
- type `task`, P2, depends on 2.3, size L
- Files: `Domain/Party.cs` (434) and `Party.Chaos.cs` (273) → `Party.cs` (facade) plus internal `TurnLocks`, `ScenarioSlot`, `FloorProgress` and `ChaosState` classes. All are guarded by the one `SyncRoot`.
- Name the repeated "member still in play" concept (`!IsDefeated && !IsOut(name)`: `Party.cs:117,299`) as `Party.IsInPlay(member)`.
- Emit a list of the ordinal/OrdinalIgnoreCase comparisons (from the findings above) in the step's PR description. They are not changed.
- **Dead members**, in a separate commit. First give the tests their own setup helpers (`PartyTestData`, `tests/.../Features/Parties/PartyTestData.cs`): `SetPending` via `TryBeginScenario` and `CompleteScenario`, and a `DamageHighestThreat` helper. Then delete `ClearLocks`, `ActionBonusCount`, `ApplyChoiceOutcome`, `TrySetPending` and `TakeDamage(int, ElementType)`, together with the tests that only exercised them.
  - Keep (and say so in the PR) any test that is the only spec of a rule still used in production, for example target selection by `Threat`. Per the findings that rule is reachable only through `TakeDamage`, so it is probably dead too. Confirm with a coverage diff before deleting.

### 2.7 Readability pass
- type `chore`, P3, depends on 2.5 and 2.6, size M
- Rename the cryptic locals (`c`, `s`, `m`, `p`, `paid`, `contested`, `early`, `done`) and `Mutate`/`PromptCoreAsync` (some already gone after 2.5).
- Replace `ExploreResponse`'s 24-argument construction with named arguments or factories (`ExploreResponse.ForPrompt`, `ForAwaiting`, `ForResolved`).
- Replace the magic strings with `NarrationSource` and `NarrationKind` enums/constants. The enum values keep their camelCase JSON, which the Web mirrors.
- Move the player-facing sentences out of handlers (`CastConfrontationVote/Handler.cs:33,45-55`, `ClaimVault/Handler.cs:35-55`) into `Features/Parties/Chaos/ChaosText.cs`.
- Tests first: existing tests; plus one golden test per moved sentence builder where none exists.

### 2.8 Front end structure
- type `task`, P2, depends on 2.3, size L (split by file)
- `DungeonRoom.razor` (631) → components by concern (dice, party panel, narration, actions) plus `DungeonRoom.razor.cs`.
- `GameSession.cs` (424) → partial classes: lock status, mana, floor, narration.
- `StageDirector.cs`: name the remaining literals (`:45` → `MaxHeroes`). It is allowed to stay >250 lines (exception, with a reason: one animation timeline with shared state).
- **Shared limits.** Add `src/DungeonForge.Web/Models/Limits.cs`, and have the Web project reference one source. Options in decision 4. Do not use a project reference from Web to Api, because it pulls in ASP.NET.
- Mana (decision 1): `MaxManaFor` moves only if the decision says so.
- Tests first: `DungeonRoomTests`, `GameSessionTests`, `PartyLobbyTests`, bUnit's existing selectors. Coverage first, so the split is verified by a test that fails without the markup.

### 2.9 Test restructure
- type `chore`, P3, depends on 2.8, size M
- Split `DungeonRoomTests.cs` (702) and `GameSessionTests.cs` (576) by behaviour. Merge `ExploreTestSupport.cs` (140) and `ChaosTestSupport.cs` (144) into shared builders in `tests/.../Support/`.
- Acceptance: the test count is identical before and after (compare `dotnet test --list-tests`). Test names are unchanged except where they move.

### 2.10 Agent docs
- type `chore`, P2, depends on 2.5, 2.6 and 1.7b, size M
- Rewrite `.claude/skills/cqrs-slice/SKILL.md` as a TDD Command/Query skill with `Result<T>` and `IEndpoint` (its current text is wrong: it documents a static extension method and `DungeonDbContext`).
- Update `CLAUDE.md`. Create `docs/map.md`. Draft `.claude/skills/add-game-rule/SKILL.md` (location `Domain/Rules/`, scripted-roller test, no RNG reorder).
- The tooling itself belongs to the separate tooling task.

**Done when:** all rules are in `Domain/Rules/`; the architecture test passes; no `src` file is over ~250 lines except the listed exceptions (`StageDirector.cs`); behaviour is unchanged; green with zero warnings.

---

# Intentional behaviour changes

Items 1-7 are Phase 1. Item 8 is Phase 2 step 2.8 (decision 4, accepted).

1. **404 responses** gain an `application/problem+json` body with `code`, `operation`, `parameters` and `traceId`. The status is unchanged.
2. **Bare 409s** gain the same body: defeated, claim `NoVault` and `ConfrontationActive`, vote `NoConfrontation`, extract `NoPact`, join duplicate or full, narration no-job.
3. **400 validation bodies** gain `code`, `operation`, `parameters` and `traceId`. `errors` is kept. The 4 explore state-conflict 400s keep status 400 and `errors.actions`.
4. **Unhandled exceptions** return a JSON ProblemDetails 500 with no stack trace, from `UseExceptionHandler`.
5. **Log lines** change text and gain an `EventId` and scope.
6. **Web:** the client decides waiting vs defeated from the code when present, with the same result.
7. **Settled in step 1.3:** a missing or malformed JSON body (and an enum value the binder rejects) now returns a `request.malformed` problem+json 400 with `errors.body`, instead of a text/plain exception message in Development and an empty body elsewhere. `RouteHandlerOptions.ThrowOnBadRequest` is on in every environment so the two agree.
8. **Lobby input limits** (Phase 2, step 2.8): party-name `maxlength` 50 → 40 and member-name 30 → 32, to match the server (`CreateParty/Endpoint.cs:7-8`). Limits come from one linked source file.

**Unchanged:** game math, RNG call order, SignalR message shapes (including the `HubException` text), the waiting 409 body, and one narrator call per turn.

# Decisions (all four recommendations accepted)

1. **Where mana lives.** Recommendation: keep it **client-only**, but move `MaxManaFor` out of `GameSession` into `Web/Services/ManaRules.cs` (one file, tested). The server holds no mana state (`GameSession.cs:60` is the only definition), and moving it server-side would be a new feature.
2. **Show `parameters` in production responses?** Recommendation: **yes, always**.
   - The values are request echoes plus public state (`turn`, `floor`, lock counts).
   - There is no auth to protect them behind (no auth middleware in `Program.cs`).
   - The allow-list test (design section) is the guard against leaking secrets such as the traitor, pockets or gaslighting content.
   - It is also what makes a production failure reproducible. The alternative is a flag, and a flag adds a branch to test.
3. **Split the explore endpoint?** Recommendation: **no**. Keep the one `POST /explore` route and the 3 handlers behind it (`GetScenario`, `LockIn`, `ResolveTurn`).
   - The route is a client contract used by `DungeonApiClient.cs:31-34`, and the body decides the command.
   - Splitting routes would be a Web + hub + test change with no token gain.
4. **Client/server shared limits (from Phase 2 step 2.8).** Recommendation: a **new tiny `DungeonForge.Contracts`-style source file linked into both projects** (`<Compile Include=... Link=...>`), with no new package and no project reference.
   - This changes the lobby's `maxlength` from 50 to 40 (party) and 30 to 32 (member), and so it is a small intentional behaviour change. Approve or veto it.
   - The alternative is to copy the constants and add a test that compares them (zero behaviour change).

# Suggested, not planned

- **Auth.** The brief and CLAUDE.md say "Auth: Clerk JWT", but `Program.cs` has no authentication. Anyone holding a `partyId` can act as any member name.
- **Tests over 250 lines.** 9 other test files exceed it (for example `ExploreDungeonNarrationTests.cs` 488, `GeminiNarratorServiceTests.cs` 480, `StageDirectorTests.cs` 381).
- **`InMemoryScoreBoard`/Supabase** parity tests; **`Web` `JsLocalNarrator`** has 4 catches with no coded logs.
- **OpenAPI.** With `IResult` endpoints (Phase 1) there is no typed-union contract. If you want a spec, add `AddOpenApi` with `ProducesProblem` metadata.
- **`IMemoryCache` eviction.** `PartyCache.cs:11` says that at the size limit a `Set` "may be dropped silently". That is a state-loss risk with no log, and a coded event would be cheap once 2.4 lands.
- **CI.** `.github/workflows/deploy-web.yml` is the only workflow. Nothing runs `dotnet test` on push. The refactor relies on local runs.

# Verification (for whoever executes)

- Per step: `dotnet build` (zero warnings) and `dotnet test --filter "FullyQualifiedName~<Slice>"`, then the full `dotnet test`.
- Per phase: `dotnet test --collect "XPlat Code Coverage" --results-directory <scratchpad>` before and after. Touched files must not lose coverage.
- Phase 1 end: `ErrorCatalogTests` and `ErrorsDocTests` pass. Each row in `docs/errors.md` has a covering test.
- Phase 2 end: `LayeringTests` passes, and `git ls-files src | xargs wc -l | sort -rn | head` shows nothing over ~250 apart from the listed exceptions.
- No push, no deploy (do not run `deploy-web`).

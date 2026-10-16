# Errors

Stub from Phase 1 step 1.1. Step 1.7a replaces this with a table generated from the error catalog. Until then it only freezes today's status codes.

Rule: a handler with no failure path may return its value directly (`CreateParty`, `GetHighScores`). Every other handler returns a `Result` (from step 1.4).

## Status codes before the refactor

Status only. Bodies change in Phase 1, see `docs/plans/refactor.md`. "VP" is `ValidationProblem` (400 with `errors`).

| Route | Failure | Status | Pinned by |
|---|---|---|---|
| POST /parties | validation | 400 VP | `CreatePartyTests` |
| POST /parties/by-code/{code}/join | validation | 400 VP | `JoinPartyTests.Join_InvalidBody_ReturnsBadRequest`, `FailureStatusTests.JoinParty_NameTooLong_Returns400` |
| | unknown code or party | 404 | `JoinPartyTests.Join_UnknownCode_ReturnsNotFound` |
| | duplicate name | 409 | `JoinPartyTests.Join_DuplicateName_ReturnsConflict` |
| GET /parties/by-code/{code} | not found | 404 | `JoinByCodeTests.ByCode_UnknownOrMalformed_ReturnsNotFound` |
| GET /parties/{id} | not found | 404 | `GetPartyStateTests.Get_UnknownPartyId_ReturnsNotFound` |
| POST /parties/{id}/unlock | not found | 404 | `TurnLockTests.Unlock_UnknownParty_Returns404` |
| POST /parties/{id}/explore | not found | 404 | `ExploreDungeonTests.Explore_UnknownPartyId_ReturnsNotFound` |
| | party defeated | 409 | `ExploreDungeonTests.Explore_DefeatedParty_ReturnsConflictForPromptAndResolve_AndDoesNotAdvanceTurn` |
| | waiting for members | 409 with `WaitingForPartyResponse` (success variant) | `TurnLockTests.DirectResolve_WithMissingLocks_Returns409Waiting_AndKeepsTurn` |
| | confrontation open | 400 VP | `VaultEndpointTests.ExploreIsBlocked_WhileAConfrontationIsOpen` (asserts >= 400) |
| POST /parties/{id}/narration | not found, no job | 404, 409 | `ClientNarrationTests` |
| POST /parties/{id}/vault/claim | not found | 404 | `VaultEndpointTests.ClaimEndpoint_ValidatesAndMapsStatuses` |
| | no vault | 409 | same |
| | confrontation active | 409 | `FailureStatusTests.ClaimVault_WhileConfrontationIsOpen_Returns409` |
| | unknown player | 400 VP | `VaultEndpointTests.ClaimEndpoint_ValidatesAndMapsStatuses` |
| | missing choice | 400 VP | `FailureStatusTests.ClaimVault_MissingChoice_Returns400` |
| POST /parties/{id}/confrontation/votes | not found | 404 | `VaultEndpointTests.VoteEndpoint_ValidatesAndMapsStatuses` |
| | no confrontation | 409 | same |
| | not eligible (the accused) | 400 VP | `FailureStatusTests.CastVote_ByTheAccused_Returns400` |
| | bad name | 400 VP | `FailureStatusTests.CastVote_InvalidPlayerName_Returns400` |
| | missing vote | 400 VP | `FailureStatusTests.CastVote_MissingVote_Returns400` |
| POST /parties/{id}/extract | not found | 404 | `TraitorPactTests.ExtractEndpoint_ReportsOutcome_OrRejectsWithoutAPact` |
| | no pact | 409 | same |
| | blank name | 400 VP | same |
| | unknown player | 400 VP | `FailureStatusTests.Extract_UnknownPlayer_Returns400` |
| claim / vote, unknown enum value | binder rejects it before the filter | 400 (framework body, not VP) | `FailureStatusTests.UnknownEnumValue_Returns400` |

## Known gaps

- Explore "no non-empty discussion" (`ExploreDungeon/Handler.cs:563`) is not covered. `ResolveAsync` already rejects blank actions at `:165`, so it looks unreachable over HTTP. Decide in step 1.4i whether to keep it.
- Narration 400 (text too long) is covered by the endpoint's existing tests only through line coverage, not by a named status test. Step 1.4h adds the body assertion.

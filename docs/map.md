# Code map

Where things live. Start here instead of searching. Update when a file or folder is added, moved or removed.

## Solution

| Path | What |
|---|---|
| `src/DungeonForge.Api` | .NET 10 Minimal API, SignalR hub, single instance, state in `IMemoryCache` |
| `src/DungeonForge.Web` | Blazor WebAssembly client (Vercel) |
| `src/Shared/Limits.cs` | Input limits, linked (`<Compile Link>`) into both projects; no project reference |
| `tests/DungeonForge.Tests` | Api tests (xUnit, FluentAssertions, `WebApplicationFactory<Program>`) |
| `tests/DungeonForge.Web.Tests` | Web tests (bUnit) |
| `docs/errors.md` | Generated error catalog (`UPDATE_ERRORS_DOC=1 dotnet test --filter ErrorsDocTests`) |
| `docs/plans/refactor.md` | The refactor plan and its decisions |

## Api (`src/DungeonForge.Api`)

| Path | What |
|---|---|
| `Program.cs` | DI (handlers are `AddSingleton`), exception handler, CORS, `AddEndpoints`/`MapEndpoints` under `/api` |
| `Endpoints/` | `IEndpoint`, reflection registration |
| `Features/<Group>/<Feature>/` | Vertical slices: `Contracts.cs`, `Endpoint.cs`, `Handler.cs`, `Errors.cs` |
| `Features/Parties/` | Party slices (`CreateParty`, `JoinParty`, `JoinByCode`, `GetPartyState`, `UnlockTurn`, `ClaimVault`, `CastConfrontationVote`, `ExtractRun`, `SubmitNarration`) |
| `Features/Parties/ExploreDungeon/` | The turn. One route `POST /explore`; `GetScenario/`, `LockIn/`, `ResolveTurn/` handlers; `TurnNarrator.cs`, `ClientNarrationCoordinator.cs`, `ExploreResponses.cs` |
| `Features/Parties/Chaos/` | `ChaosDispatcher`, `ChaosText` (all player-facing chaos sentences) |
| `Features/Parties/Realtime/` | `DungeonHub`, `IPartyNotifier`, `PlayerConnections` |
| `Features/Parties/PartyStore.cs` | `IPartyStore` (`TryGet`, `Save`, `Touch`): the only access to party cache entries |
| `Features/Social/` | Social layer: seed, peers, asks, abilities, supervision |
| `Features/Scores/`, `Features/Classes/`, `Features/Health/` | High scores, class spells, health |
| `Domain/` | `Party` (+`Party.Chaos`, `Party.Traitor`) facade with internal `TurnLocks`, `ScenarioSlot`, `FloorState`, `ChaosState`; one `SyncRoot` lock; `IRoller` |
| `Domain/Rules/` | Pure static game math: `RoomRules`, `ActionOdds`, `PartyRules`, `MemberRules`, `ConfrontationRules`, `Perception`, `TraitorPact`, `CovertResolver`, `RoomSecrets`, `Gaslighting` |
| `Errors/` | `Result<T>`, `Error`/`ErrorSpec`, `ErrorCodes`, `ErrorMapping` (`ToHttp`), `Param` allow-list, `LogEvents`, `BestEffort`, `PipelineExceptionHandler` |
| `Infrastructure/Narration/` | Gemini Flash narrator (`GeminiNarratorService`, `KeyPool`), prompts, static/fallback narrator |
| `Infrastructure/Scores/` | Supabase score board |

Layering (enforced by `LayeringTests`): `Domain` and `Errors` never reference `Features` or `Infrastructure`.

## Web (`src/DungeonForge.Web`)

| Path | What |
|---|---|
| `Pages/DungeonRoom.razor` + `.Actions/.Hub/.Narration/.Sound.cs` | The game screen, split by concern |
| `Pages/PartyLobby.razor` | Create/join |
| `Services/GameSession*.cs` | Client game state, partials: `Locks`, `Mana`, `Narration`, `Tally`, `Chaos` |
| `Services/ManaRules.cs` | Mana math (client-only) |
| `Services/DungeonApiClient.cs`, `DungeonHubClient.cs` | REST and SignalR clients |
| `Components/` | Panels, `SpellbookDrawer`, `SocialPanel`; `Stage/StageDirector.cs` is the one allowed >250-line file (one animation timeline) |
| `Models/` | DTOs mirroring Api JSON, `NarrationSources` wire strings |

## Tests

| Path | What |
|---|---|
| `tests/DungeonForge.Tests/Domain/Rules/` | One test file per rules file, hand-written expected numbers |
| `tests/DungeonForge.Tests/Features/` | HTTP/hub tests per slice; `*ErrorTests.cs` carry `[CoversError]` |
| `tests/DungeonForge.Tests/Errors/` | Catalog, mapping, docs-generation tests |
| `tests/DungeonForge.Tests/Support/` | `PartyTestBuilders`, `TestRollers` (`FixedRoller`, `SequenceRoller`, `ScriptedRoller`), notifiers |
| `tests/DungeonForge.Tests/Architecture/` | Layering rules |

## Skills (`.claude/skills`)

`cqrs-slice` (new slice), `add-error` (new failure), `add-game-rule` (new rule), `diagnose-failure` (trace a failure),
`dotnet-build-troubleshooting`, `deploy-web`.

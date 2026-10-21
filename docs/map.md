# Code map

Where things live. Start here instead of searching. Update when a file or folder is added, moved or removed. The big picture (layers, game loop, how Web and Api connect) is in [README.md](../README.md).

## Solution

Dependencies point inward: `Api -> Infrastructure -> Application -> Domain -> Shared` (and `Api -> Application`). `LayeringTests` enforces it.

| Path | What |
|---|---|
| `src/DungeonForge.Shared` | `Limits.cs`: input limits shared by Domain and Web |
| `src/DungeonForge.Domain` | Entities and pure game rules; no framework |
| `src/DungeonForge.Application` | Use cases (handlers), contracts, errors, ports; no ASP.NET |
| `src/DungeonForge.Infrastructure` | Port adapters: Gemini narration, Supabase scores, memory-cache party store |
| `src/DungeonForge.Api` | .NET 10 Minimal API host, SignalR hub, single instance |
| `src/DungeonForge.Web` | Blazor WebAssembly client (Vercel) |
| `tests/DungeonForge.Tests` | Backend tests (xUnit, FluentAssertions, `WebApplicationFactory<Program>`) |
| `tests/DungeonForge.Web.Tests` | Web tests (bUnit) |
| `docs/errors.md` | Generated error catalog (`UPDATE_ERRORS_DOC=1 dotnet test --filter ErrorsDocTests`) |
| `docs/plans/refactor.md` | The earlier Result/Error refactor plan (historical; paths predate the project split) |

## Domain (`src/DungeonForge.Domain`)

| Path | What |
|---|---|
| `Parties/` | `Party` facade (`Party.Chaos`, `.Traitor`, `.Setup`) with internal `TurnLocks`, `ScenarioSlot`, `FloorState`, `ChaosState`, `SetupState`; one `SyncRoot` lock; `PartyMember`, `PartySnapshot`, `PlayerAction`, `PendingEncounter`, `CovertIntent`, `ChaosTypes` |
| `Characters/` | `CharacterClass`, `Spell`, `SpellCatalog`, `ElementType`, `DamageResult` |
| `Dungeon/` | `RoomType` |
| `Common/` | `IRoller`, `RandomRoller` |
| `Rules/` | Pure static game math: `RoomRules`, `ActionOdds`, `PartyRules`, `MemberRules`, `StoryLean`, `ConfrontationRules`, `Perception`, `TraitorPact`, `CovertResolver`, `RoomSecrets`, `Gaslighting` |

## Application (`src/DungeonForge.Application`)

| Path | What |
|---|---|
| `Features/<Group>/<Feature>/` | One slice: `Contracts.cs` (request/response), `Handler.cs`, `Errors.cs` |
| `Features/Parties/` | Slices `CreateParty`, `JoinParty`, `JoinByCode`, `GetPartyState`, `GetSetup`, `SubmitSetup`, `UnlockTurn`, `ClaimVault`, `CastConfrontationVote`, `ExtractRun`, `SubmitNarration`; group-level shared files: `IPartyStore`, `Contracts.cs` (shared DTOs), `PartyMapping`, `InviteCodes`, `SetupQuestions`, `HiddenClasses` |
| `Features/Parties/ExploreDungeon/` | The turn. One route `POST /explore`; `GetScenario/`, `LockIn/`, `ResolveTurn/` handlers; `TurnNarrator.cs`, `ClientNarrationCoordinator.cs`, `ExploreResponses.cs` |
| `Features/Parties/Chaos/` | `ChaosDispatcher`, `ChaosText` (all player-facing chaos sentences) |
| `Features/Parties/Realtime/` | `IPartyNotifier` port (the SignalR implementation is in Api) |
| `Features/Social/` | Social layer: `Seed`, `Peers`, `Asks`, `Abilities`, `Supervision`, `Core`; wired by `SocialRegistration.cs` |
| `Features/Scores/`, `Classes/`, `Health/` | High scores (`IScoreBoard` port), class spells, health |
| `Narration/` | `IDungeonNarrator` port and its models, prompt builders, parsers, fallback and static narrator, `IJsonModel` port, `NarrationOptions` |
| `Errors/` | `Result<T>`, `Error`/`ErrorSpec`, `ErrorCodes`, `ErrorStatus`, `ValidationFailure`, `Param` allow-list, `LogEvents`, `BestEffort`, shared error factories |
| `DependencyInjection.cs` | `AddApplication`: every handler is registered here |

## Infrastructure (`src/DungeonForge.Infrastructure`)

| Path | What |
|---|---|
| `Narration/` | Gemini Flash narrator (`GeminiNarratorService`, `GeminiJsonModel`, `KeyPool`, `KeyPoolGeminiNarrator`), key resolution |
| `Scores/` | `SupabaseScoreBoard`, `InMemoryScoreBoard` fallback, `schema.sql` |
| `Parties/PartyStore.cs` | `IPartyStore` over `IMemoryCache` |
| `DependencyInjection.cs` | `AddInfrastructure` |

## Api (`src/DungeonForge.Api`)

| Path | What |
|---|---|
| `Program.cs` | Composition root: `AddApplication`, `AddInfrastructure`, JSON, SignalR, CORS, endpoints under `/api` |
| `Features/<Group>/<Feature>/<Feature>Endpoint.cs` | One `IEndpoint` per slice, mirroring the Application path; request validation filter lives here |
| `Endpoints/` | `IEndpoint`, reflection registration |
| `Realtime/` | `DungeonHub`, `SignalRPartyNotifier`, `PlayerConnections` |
| `Http/` | `ErrorMapping` (`ToHttp`), `ValidationErrors`, `PipelineExceptionHandler` |

## Web (`src/DungeonForge.Web`)

| Path | What |
|---|---|
| `Core/` | `DungeonApiClient` (REST), `DungeonHubClient` (SignalR), `ApiResult` |
| `Shared/` | `GameTypes.cs`: `RoomType`, `CharacterClass`, `MemberDto` |
| `Features/Lobby/` | `PartyLobby.razor` (`/`), create/join contracts |
| `Features/Dungeon/` | `DungeonRoom.razor` + `.Actions/.Hub/.Narration/.Sound.cs` (the game screen), `GameSession*.cs` (client state, partials), panels, `ScenePainter`, local narrator, turn contracts |
| `Features/Dungeon/Stage/` | ASCII stage; `StageDirector.cs` is the one allowed >250-line file (one animation timeline) |
| `Features/Social/`, `Features/Scores/` | Social panel and cards, high scores card, their contracts |
| `Layout/` | `MainLayout.razor` |

## Tests

| Path | What |
|---|---|
| `tests/DungeonForge.Tests/Domain/` | Party state tests; `Rules/` has one test file per rules file, hand-written expected numbers |
| `tests/DungeonForge.Tests/Features/` | HTTP/hub tests per slice; `*ErrorTests.cs` carry `[CoversError]` |
| `tests/DungeonForge.Tests/Errors/` | Catalog, mapping, docs-generation tests |
| `tests/DungeonForge.Tests/Infrastructure/` | Bootstrap, CORS and narration adapter tests |
| `tests/DungeonForge.Tests/Support/` | `PartyTestBuilders`, `TestRollers` (`FixedRoller`, `SequenceRoller`, `ScriptedRoller`), notifiers |
| `tests/DungeonForge.Tests/Architecture/` | Layering rules |

## Skills (`.claude/skills`)

`cqrs-slice` (new slice), `add-error` (new failure), `add-game-rule` (new rule), `diagnose-failure` (trace a failure),
`dotnet-build-troubleshooting`, `deploy-web`.

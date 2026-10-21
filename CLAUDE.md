# DungeonForge (.NET 10 + Blazor WASM)

## Build & Test Rules
- Build solution: `dotnet build`
- Run unit tests: `dotnet test`

## Infrastructure Topology (Free-Tier Optimized)
- Frontend: Blazor WebAssembly static assets deployed to Vercel.
- Backend: .NET 10 Minimal API hosted as a single-instance container (Render / Fly.io / Azure Free App Service).
- Auth: Clerk JWT validation.
- Database: Supabase PostgreSQL (reserved strictly for static user metadata & high scores).
- Ephemeral State: `IMemoryCache` on single-instance container (zero turn-by-turn DB writes).

## Architectural Standards
- Pattern: Vertical Slice CQRS (No MediatR). Handlers, contracts and errors live in `src/DungeonForge.Application/Features/<Group>/<FeatureName>/`; the endpoint in the same path under `src/DungeonForge.Api/Features/`.
- Endpoints: Minimal API slices utilizing `IEndpoint` and `AddEndpointFilter` for cross-cutting checks.
- Handlers return `Result<T>`; failures are coded `Error`s (`Errors/`), mapped to ProblemDetails by `.ToHttp(http)`. See `docs/errors.md`.
- Party state goes through `IPartyStore`; mutate under `party.SyncRoot`. Game rules and math are pure static classes in `src/DungeonForge.Domain/Rules/` taking `IRoller`; never reorder RNG calls.
- Dependencies point inward: Api -> Infrastructure -> Application -> Domain -> Shared (`LayeringTests`); Domain and Application never touch ASP.NET. Keep `src` files under ~250 lines (exception: `StageDirector.cs`).
- Zero build warnings. Input limits live in `src/DungeonForge.Shared/Limits.cs`, referenced by Domain and Web.
- Architecture overview: `README.md`. Where things are: `docs/map.md`. Skills: `cqrs-slice`, `add-error`, `add-game-rule`, `diagnose-failure`.
- AI Narration: Stateless, concise calls to Google Gemini Flash (free tier) via REST (key `GoogleFlash:ApiKey`). Game rules and math executed in C#.

## Token Conservation Protocols
- Do not explain basic C# syntax or framework design patterns. Provide direct code implementations.
- Always run `/clear` in the interactive session after completing a vertical slice to keep context usage near zero.
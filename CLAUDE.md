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
- Pattern: Vertical Slice CQRS (No MediatR). Features live under `src/DungeonForge.Api/Features/<Group>/<FeatureName>/`.
- Endpoints: Minimal API slices utilizing `IEndpoint` and `AddEndpointFilter` for cross-cutting checks.
- AI Narration: Stateless, concise calls to Claude Haiku 5.5 via Anthropic C# SDK. Game rules and math executed in C#.

## Token Conservation Protocols
- Do not explain basic C# syntax or framework design patterns. Provide direct code implementations.
- Always run `/clear` in the interactive session after completing a vertical slice to keep context usage near zero.
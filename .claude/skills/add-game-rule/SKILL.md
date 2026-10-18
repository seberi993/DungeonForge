---
name: add-game-rule
description: Add or change a game rule (damage, odds, gold, room, chaos, traitor math) as a pure static class in Domain/Rules with a scripted-roller test. Use when a rule changes or a handler needs new game math.
---

Game rules and math run in C#, never in the narrator and never inline in a handler. They live in `src/DungeonForge.Api/Domain/Rules/`.

1. **Find it first.** `Grep` `Domain/Rules/` for the concept (`RoomRules` rooms/gold/damage, `ActionOdds` success odds, `PartyRules` party limits,
   `MemberRules` hp/threat, `ConfrontationRules`/`Perception`/`TraitorPact`/`CovertResolver`/`RoomSecrets`/`Gaslighting` chaos mode).
   Extend the matching file; add a new `static class <Name>Rules` only for a new concept.
2. **Test first (red).** `tests/DungeonForge.Tests/Domain/Rules/<Name>Tests.cs`, one file per rules file, expected numbers written out by hand
   (`[Theory]`/`[InlineData]`), not recomputed with the formula under test (copy `RoomRulesTests.cs`).
   - Anything that rolls takes `IRoller` and is tested with a scripted roller from `tests/.../Support/TestRollers.cs`:
     `FixedRoller` (room + success), `SequenceRoller` (queued room rolls), `ScriptedRoller` (replays values modulo `max`).
   - Pin the roller's bound and call count (a counting roller recording each `max`).
3. **Implement (green).** `public static` methods, constants named (`WoundedRatio`), no I/O, no `DateTime`, no logging.
   Domain may not reference `Features` or `Infrastructure` (`LayeringTests`).
4. **Wire.** Replace the inline math in the handler with a call. If a handler endpoint test pinned the old numbers it must still pass unchanged
   (`ExploreDungeonRulesTests` pins room math through the endpoint).
5. **Do not reorder RNG calls.** Number and order of `roller.Next(...)` calls per turn is behaviour: scripted tests and replayed seeds depend on it.
   New randomness is appended after the existing calls, and a test pins the new call order.
6. `dotnet test --filter "FullyQualifiedName~Rules"`, then `dotnet build` (zero warnings) and full `dotnet test`. Update `docs/map.md` if a file was added.

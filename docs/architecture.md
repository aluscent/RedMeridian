# Game architecture

The game runs in one ASP.NET Core process. The browser renders the selected map; the server owns all campaign state and advances it on one clock. Do not add a process or network service for a game feature.

## Current boundaries

- `Source/Program.cs` wires HTTP routes and the simulation timer. It does not implement gameplay commands or disk-save rules.
- `Source/Application/ColonySession.cs` owns the campaign session, synchronization, selected base, and command response envelope. It invokes feature command handlers while holding the session lock.
- `Source/Application/*Commands.cs` owns command validation and calls domain operations for its feature. Add feature behavior here rather than to the HTTP entry point.
- `Source/Application/SaveRepository.cs` owns save paths, disk writes, and backup creation. `Source/Domain/SaveData.cs` is the compatibility-sensitive wire format; `ColonySave.cs` owns colony conversion and validation.
- `Source/Domain` implements the simulation. `SpaceProgram` coordinates bases and fleet; `Colony` coordinates buildings and resources. These are still broad aggregates and will be separated in later stages of issue #31.
- `Source/Config` owns ordinary buildings, materials, research, ships, and world settings. Resource icon filenames are `wwwroot/assets/resource-{id}.png`; unknown or missing icons fall back to text.
- `Source/wwwroot` owns the browser UI. Its shared globals are a remaining coupling point.

## Safe extension sequence

For a catalog-only resource, add its catalog entry and optional icon. For a new gameplay action, add a feature command handler and route the action through the session; keep the HTTP response shape (`message`, `edits`, `state`). A new persisted field must be validated on restore, round-trip in a current save, and retain a defined default for legacy saves. Do not renumber existing building IDs or reorder saved material identities without a migration.

Issue #31 tracks the remaining migration: narrow domain feature state, feature-owned save sections, stable content keys independent of packed runtime indices, and browser modules with explicit dependencies. Each change must preserve the current save versions and compare against the headless stress and live browser tests.

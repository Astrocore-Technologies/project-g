# ADR 0007: First PvE checkpoint

## Status / scope

Stage 6 and checkpoint 7.1 accepted. Checkpoint 7.2 extends this baseline with a separate boss; see `0007-boss-encounter.md`. No persistence, engine, shard or service-boundary change.

Temporary assumptions: one nonblocking monster, proximity aggro and sticky nearest target rather than a threat table; no loot, EXP, invulnerability, regeneration, respawn or encounter reset. Returning home does not heal. HP=0 stops actions; restarting a client creates the existing fresh session, restarting the server resets the prototype arena. These are not permanent death/respawn rules.

## Server

Update 2026-10-09: the historical no-regeneration/no-respawn assumption above is superseded by the approved gameplay contract. Connected living players recover HP at the existing derived rate, coalesced once per simulation second. Configured NPC spawns use a stable regional key and a persisted UTC respawn deadline (ordinary 30 s, boss 120 s; 0 disables). Only injured players and waiting spawns are scheduled. Death/respawn transitions join the existing atomic regional checkpoint; no new service, SQL migration or wire layout. A respawn retires the old runtime ID, clears combat/effects/motion/AI and creates a fresh ID at home. Existing AOI despawn/CombatState/snapshots handle both observers and late joins. Old world JSON remains compatible; the optional bounded `NpcRespawns` component has independently versioned entries. Returning an injured living NPC home still does not heal it.

`NpcSimulation` is transport/Godot-independent. `ServerWorld` owns the runtime ID, spatial index and tick ordering. NPCs do not have fake PlayerIds. The existing catalog supplies creature stats and weapon; `Npc` configuration supplies spawn/speed/perception/leash/decision interval/windup. The shipped prototype enables one NPC at (7, 3), speed 3, aggro 6, leash 10, decisions no more than 5 Hz and windup 1 second. Tests opt in explicitly; old arenas remain unchanged.

States: Idle, Chasing, Windup, Returning, Defeated. Spatial queries find nearby live players; perception requires LOS, ties use numeric ID. An acquired target remains until disconnected, defeated, unreachable or outside the home leash. Chase routes use the existing radius-aware A* and shared movement, recalculated on decisions rather than every frame. Return walks home without reacquiring until home. No teleports across geometry.

Movement executes first, then player attacks/abilities, then NPC strike resolution. Windup stops NPC movement and locks direction; it does not home toward the player. The completed swing rechecks current player position, cone/range, LOS and HP. Killing the NPC before release cancels it. Combat cooldown and damage use existing server calculations and RNG. One nearest eligible player can be hit, including interception; player attacks/spells can hit monsters or dummies, never other players. Clients cannot command a monster actor.

Defeated NPCs remain visible with zero HP, stop attacking/moving and never reacquire. Dead players fail movement/attack/ability validation and are ignored by perception. No EXP/drop consequences are invented. A single NPC is a bounded prototype, not a crowd-capacity claim; a multi-NPC scheduler/activation index and profiling are future scaling work.

## Protocol v7 / AOI

Existing numeric IDs and byte layouts remain stable. `CombatEntityKind.Monster = 2` is new, so v6 peers are rejected by handshake. `CombatState` supplies NPC spawn/profile/HP; no PlayerSpawn is fabricated. NPC position is carried in the existing 44-byte EntitySnapshot (irrelevant movement/ability acknowledgement fields are zero). MTU-aware chunks stay Unreliable; known entities independently filter ticks.

New **24 / NpcWindup**, ReliableOrdered, total **42 bytes**: ushort header, ulong actor, uint sequence/tick, float origin X/Z, float unit direction X/Z, float range/remaining seconds. Body is exactly 40 bytes. Invalid IDs, directions, nonfinite values, remaining outside [0,10], all truncations and trailing bytes are rejected. Remaining=0 ends/cancels the telegraph and never applies damage.

Per-observer version tracking sends windup begin/end changes and current remaining windup on AOI entry, not a reliable update each tick. Confirmed strikes reuse AttackEvent (17); health-only correction is used when the observer sees only the target. Spawn/profile and action events share the reliable ordered stream. No target/threat lists, stats, definitions or future AI decisions are sent.

## Client

`NpcPresentation` interpolates a bounded 20-point snapshot history, does no AI and never predicts monster hits. It conservatively avoids interpolating through walls. Red mesh distinguishes the hostile monster from the purple dummy. Existing combat presentation renders a red cone for windup and server-confirmed HP/damage; zero HP clears telegraphs and labels the actor Defeated. Local HP=0 clears movement prediction/history and blocks new move/attack/skill input. Scene/project/UID/AGENTS files are untouched.

## Verification

Build and tests use `.artifacts/stage7` without stopping user processes. NuGet audit is explicitly disabled during isolated build; this is not a dependency-security assessment. Server `--validate-content` validates the enabled NPC's content/configuration/navigation before opening UDP.

Domain/wire coverage: acquisition/chase/windup/release, dodge out of locked swing, wall detour, leash/disconnect return, kill cancellation, dead-player rejection, no automatic respawn, NPC AOI/snapshots, invalid startup configuration, solo/cooperative victory without stat scaling and exact bounded wire parsing. Two-client UDP test uses 100–150 ms latency and 10% loss, checks windups/identical confirmed hits and independent NPC snapshots. Godot is unavailable in PATH: visual acceptance remains with the user.

Executed: `dotnet build Game.slnx --artifacts-path .artifacts/stage7 -m:1 -p:NuGetAudit=false` (0 errors/warnings), `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage7 --no-build` outside the sandbox (172 passed), `dotnet .artifacts/stage7/bin/Content.Server/debug/Content.Server.dll --validate-content` (success without UDP), and `git diff --check` (no whitespace errors). Existing user processes were not stopped.

## Recovery update verification — 2026-10-09

- `dotnet build Game.slnx --no-restore -m:1`: passed, 0 errors. Existing NU1903 warning for SQLitePCLRaw.lib.e_sqlite3 2.1.11 and NU1900 audit-endpoint warning remain; dependencies were not changed.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --logger 'console;verbosity=minimal'`: final run 537 passed, 7 skipped, 0 failed. Skips require opt-in PostgreSQL or a Development-save backup; neither was supplied. New tests cover VIT/fractional HP, combat/casting/movement, dead/offline/full-health exclusion, equipment/reconnect, ordinary/boss/disabled respawns, invalid saves, actual SQLite restart, stale target identity and two lossy observers behind a delayed checkpoint.
- Earlier runs exposed old constant-HP assertions and a trade-test race between independent peer streams. Damage tests now isolate passive healing; the trade test waits for both offer revisions before acceptance. Production trade behavior is unchanged.
- `dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content`: passed.
- `& 'D:/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe' --headless --path D:/projects/project-g/Content.Client res://Tests/UI/PlayerHudSmoke.tscn -- --server-port=28956 --identity=hp-respawn-hud-1`: passed with `PLAYER_HUD_OK`. Used an isolated Development server and `.artifacts/hp-respawn-live.db`, then stopped that server. Checks actual resource HUD, stat preview/apply and cleanup; headless smoke is not visual playtesting of a respawning monster.
- `git -c core.safecrlf=false diff --check`: passed. Working player saves and user processes were not modified.

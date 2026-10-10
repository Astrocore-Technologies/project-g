# ADR 0005: Manual authoritative prototype attack

## Status

Stage 5 accepted by the user. Stage 6 extends this baseline; see ADR 0006.

2026-10-10 input amendment: plain LMB only selects a combat target for approach/autoattack; empty-ground clicks do not swing. Ctrl + LMB bypasses both target picking and NPC dialogue, issuing one directional swing without starting pursuit/repeat. Existing cooldown, alive/defense/modal checks and server authority remain; no protocol, persistence, attack formula or animation change. The F1 guide documents the chord.

Verification of the input amendment:
- `dotnet build Game.slnx --no-restore -m:1` passed with 0 errors; existing NU1903 (SQLitePCLRaw 2.1.11) and NU1900 (audit endpoint unavailable) warnings remain. After updating the smoke fixture, `dotnet build Content.Client/Project-G.csproj --no-restore -m:1` passed without warnings/errors.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Combat|FullyQualifiedName~Quest|FullyQualifiedName~Defense' --logger 'console;verbosity=minimal'`: 103 passed.
- Full run with the same options but no filter: 538 passed, 7 skipped, 1 failed (`RecoveryReplicationTests.TwoLossyClientsReplaceDeadNpcOnlyAfterDurableRespawn`, line 91: old/new NPC IDs equal). The separate `FullyQualifiedName~RecoveryReplicationTests` rerun also failed there; the server and this test were not changed by the input amendment.
- `dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content` passed.
- Godot 4.7.1 Mobile/D3D12: `res://Tests/UI/CombatInputSmoke.tscn -- --server-port=28956 --identity=ctrl-attack-2 --attack-input-only` passed against an isolated Development server/database. Real input checks cover ground clicks, directional Ctrl swings and held chord, NPC dialogue bypass, modal protection, no Ctrl pursuit, normal approach/repeated autoattack, and cancellation of autoattack by Ctrl. Use a graphical, focused window and a fresh isolated identity/database for this smoke; the default scene mode remains the older defense/reconnect scenario.

2026-10-09 amendment: the user approved selected-target repeated basic attacks and automatic approach,
RMB-held movement, Tab guard, Shift parry and stamina. The old manual-only input restrictions below
are historical, superseded by `docs/design/gameplay-contract.md` (Combat).
Authority, fixed ticks, geometry validation and spatial queries remain unchanged.
Protocol v27 appends optional target ID to AttackCommand (body 24 bytes), GuardImpact to AttackEvent
(62 bytes), and adds DefenseCommand 84 (13 bytes) / owner-only DefenseState 85 (86 bytes).
Guard heartbeats and resource snapshots are Unreliable; parry/release use ReliableOrdered.
Guard expires after a bounded lease if updates stop. Costs/cooldowns are checked on the server tick.
Additive SavedDefense v1 in CharacterState stores stamina and remaining recovery/parry timers;
legacy characters start full, reconnect cannot refill, database schema is unchanged. Snapshots still
follow the durable publication barrier. Old client/server pairs are rejected by protocol version.

Verification of the amendment (2026-10-09):
- `dotnet build Game.slnx --no-restore -m:1 -p:OutputPath=D:/projects/project-g/.artifacts/defense-tests-final/`: passed. The ordinary server output was locked by an existing server, which was left running.
- `dotnet test Content.Tests/Content.Tests.csproj --no-restore -m:1 -p:OutputPath=D:/projects/project-g/.artifacts/defense-tests-final/ --logger "console;verbosity=minimal"`: 466 passed, 7 skipped (live PostgreSQL and opt-in development database copy). One earlier concurrent run timed out in the child-process SQLite test; its isolated rerun and two subsequent full runs passed.
- Server `--validate-content`: passed. Existing NU1903 SQLitePCLRaw vulnerability and NU1900 audit-access warnings remain outside this change.
- Godot 4.7.1 headless `Tests/UI/PlayerHudSmoke.tscn`: passed against isolated SQLite server/profile, including resource bars and allocation preview/apply. Percentage formatting also passed.
- D3D12 Mobile `Tests/UI/CombatInputSmoke.tscn`: initial graphical run passed parry, guard release, automatic approach/repeat, RMB cancellation, recovery and dash cost. The later extended cursor-tracking/reconnect run closed without its completion marker and is not counted as passed; defense serialization/restore and reconnect-without-refill are covered by server tests. HUD screenshot was inspected and overlapping sidebar labels repositioned.
- Fixed-tick validation and owner-only wire tests cover frontal/back hits, one-hit parry, cooldown, insufficient stamina, expired guard, magic bypass, regen, requested-target validation and malformed/truncated packets.

## Scope and explicit prototype assumptions

A melee training sword and one stationary training target in the flat test region. This is not a permanent PvP/death rule or the NPC/AI stage. Players cannot damage other players in this training slice. A click on empty ground still swings. There is no auto-walk, automatic repeat, resource cost, windup, regeneration, respawn, loot or attack-induced movement stop. An existing RMB route may continue while swinging. Holding LMB does not repeat in this implementation.

`Combat` in server configuration selects player/target definitions and position, cone half-angle (45 degrees), and critical multiplier (1.5). These are temporary configurable prototype values. Target default position is X=-7, Z=3. Depleted target stays at HP=0; restarting the server resets the training arena, not a character persistence or death mechanic.

## Ownership and simulation

- `ServerWorld` owns runtime IDs, connection ownership and the shared spatial index. The target receives an ordinary opaque runtime ID without a fake PlayerId.
- `CombatSimulation` owns separate runtime combatant state, calculated attributes, weapon, HP, cooldown, sequence tracking and queued intentions. It does not depend on UDP or Godot.
- Packet polling only queues an intention. After movement, fixed simulation resolves combat using current authoritative positions. ClientTick is not used to speed up cooldown or rewind positions; bounded lag compensation belongs to stage 6.
- Commands cannot name a target, origin, weapon or damage. Sender connection determines the player entity. Invalid ownership, old/zero sequence and non-unit/non-finite direction are rejected.
- At most one intention per actor per server tick; one pending slot and one coalesced private result per actor. Rejected flood requests do not grow a history/queue. Replays cannot execute again. Results are cleared after delivery; disconnect removes pending state.
- Accepted commands consume server cooldown even on miss. The minimum execution interval is one fixed tick; very high attack-speed stats do not yield multiple attacks inside one tick.
- Broad-phase queries use the spatial index, followed by target-center cone/range checks, target state, training-target policy and navigation LOS. Current LOS uses conservative radius-aware navigation sweep, not a Godot raycast.
- One nearest target is hit; equal distances break ties by numeric entity ID. The target cannot be a player in this prototype.
- Power, defense, critical chance and interval use accepted stage-4 data/calculations. Critical RNG executes on the server; tests inject a deterministic roll. Critical multiplier is applied before defense. Damage is finite/non-negative and capped at remaining target HP; HP never drops below zero.
- The catalog/profile/configuration is validated before opening a port. This slice rejects non-melee actor profiles and ranges exceeding the spatial query budget rather than failing inside tick.

## Protocol version 5

Existing IDs and movement/navigation layouts are unchanged. Version-4 peers fail the versioned handshake.

All messages below include a ushort type header. They use ReliableOrdered: manual commands/actions are low-frequency events, not frequent movement snapshots. Spawn, combat state and confirmed actions share the server reliable stream; snapshots remain Unreliable, movement commands remain Sequenced.

| ID | Message | Body fields in order | Total bytes |
|---|---|---|---:|
| 15 | AttackCommand | uint sequence, uint clientTick, float direction X/Z | 18 |
| 16 | AttackResult | uint sequence, uint serverTick, byte outcome | 11 |
| 17 | AttackEvent | ulong attacker, uint sequence/tick, float origin X/Z, float direction X/Z, float range, ulong target, double damage/targetHP, byte critical | 63 |
| 18 | CombatState | ulong entity, uint tick, byte kind, float position X/Z, double HP/maxHP/attackInterval, float range/halfAngleRadians | 55 |

Exact body lengths are checked before reading. IDs, enums, direction, booleans, finite numeric values and health ranges are validated. Target ID zero is reserved for an accepted miss with zero damage/health and no critical flag.

`AttackResult` goes only to the owning connection. Results during command floods are deliberately coalesced, not an unbounded acknowledgement history. `AttackEvent` goes only to AOI observers seeing the actor and hit target. An observer seeing only the affected target receives updated CombatState instead, without exposing an out-of-AOI attacker. Initial spawn and AOI reentry include current health/profile. Training targets use CombatState as their static spawn; existing PlayerDespawn removes either kind.

Only revealed position/health and public attack presentation parameters leave the server. No definitions, primary/derived stats, scaling coefficients or RNG are transmitted. CombatState is sent on spawn/reentry or relevant health correction, not every movement tick.

## Client

- `CombatPresentation` is a separate Godot node, not combat logic added to the movement controller.
- LMB projects the cursor to the ground and sends a normalized direction. RMB remains unchanged.
- One bounded pending prediction per local player. The blue cone is speculative presentation, never speculative damage/HP. Confirmed gold cone uses authoritative origin/direction; rejected actions cancel the speculative visual. Delayed results cannot leave unlimited pending history (2-second timeout).
- Attack sequence and spawn tick prevent replay of old visual events. Authoritative health/damage/critical labels are applied only to known relevant actors. Reentry initializes current health.
- Cone mesh is built from the revealed range/angle at profile initialization; effects are short-lived and reuse the mesh/material/node. HP labels update on events. No scene/project.godot or generated UID changes are required.
- This is placeholder readable combat feedback, not final animation/VFX. Prediction does not implement windup, rollback of gameplay state or server rewind.

## Verification / limitations

Domain tests: tick-only execution, no autoattack/approach, cone/range/LOS, ownership, cooldown, client-time spoofing, sequence replay, invalid direction, bounded flood, nearest/tie target selection, no PvP damage, critical roll, non-negative HP, pending cleanup and AOI target inclusion.

Wire tests: exact round trips, packet budgets, all truncation lengths, trailing forged origin/damage fields, invalid IDs/enums/booleans/non-finite values/health.

Two-client UDP test at 100–150 ms simulated receive latency and 10% packet loss checks identical confirmed attacks/damage, private acknowledgements, replay protection and current HP on AOI reentry. Existing movement/AOI/navigation tests remain.

Godot visual acceptance is separate when no executable is available. There is no production capacity claim; warm tick allocation behavior and larger combat crowds need profiling beyond this prototype.

Executed: `dotnet build Game.slnx --artifacts-path .artifacts/stage5 -m:1` (0 errors; NU1900 audit-source warning), `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage5 --no-build` outside the sandbox (126 passed), and the built server with `--validate-content` (successful catalog/world/profile validation without UDP startup). `git diff --check` passed. Godot executable was not found in PATH, so the scene was not launched; compilation is not visual acceptance. Existing running user processes were not stopped.

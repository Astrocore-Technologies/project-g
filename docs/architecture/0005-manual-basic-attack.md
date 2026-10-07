# ADR 0005: Manual authoritative prototype attack

## Status

Implemented for stage 5; awaiting user acceptance. Stage 4 has been accepted.

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

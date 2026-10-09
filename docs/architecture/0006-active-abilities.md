# ADR 0006: Prototype active abilities and authoritative dash

## Status and scope

Update (2026-10-09): the no-mana-regeneration assumption below is superseded by the approved progression rules in `../design/gameplay-contract.md`. The server now restores mana on fixed simulation ticks for living, connected players; only actors with missing mana are scheduled for recovery. Authority and wire contracts are unchanged.

Stages 5 and 6 accepted by the user. Stage 7 extends the target policy to hostile NPCs; see ADR 0007. No engine, authority, persistence or process-boundary change. This is a training slice, not final skill/PvP/death design.

Explicit temporary assumptions: Q projectile, W cursor-ground AoE, Space cursor-direction dash; only training targets receive damage. No spell critical RNG, mana regeneration, invulnerability, interrupt refunds or auto-approach. An existing RMB route may continue during a spell. Dash cancels that route; a new RMB during dash is sent repeatedly and executes after dash ends. Cast blocks starting another ability or basic attack; already released projectiles remain independent.

## Data and ownership

- Server catalog schema 2 adds unique ushort `networkId`, `speed`, `magicAttackScale`. Balance version remains 1. No persistent-data migration: only the shipped immutable definition file changes. Older definition schemas fail explicitly.
- Player loadout resolves server-owned definition references. At most 8 revealed slots. Unsupported ranges/lifetimes/dash cast profiles fail before UDP opens.
- `AbilitySimulation` owns mana, per-slot deadline cooldowns, cast/effect phases, bounded intentions, collision and damage. `ServerWorld` maps connection ownership and executes dash through its authoritative `NavigationMover`.
- Shared contains only revealed bounded messages and deterministic dash clipping/segment-circle geometry. Definitions, formulas and stats stay server-only. Clients send ID/aim, never origin, hit, resource result, damage or assigned position.
- Mana cap/current supply in this slice is `max(0, derived MaxMana)`. The signed derived stat is unchanged. Accepted casts consume mana/cooldown once; rejection consumes neither. No regen or per-player cooldown tick scan.
- Spell power: `max(0, definition.Power + MagicAttack * MagicAttackScale)`, then existing signed MDEF formula; actual damage is capped at remaining HP. AoE hits each eligible target once; projectile hits the nearest first swept contact, with ID tie-breaking. Players are never eligible training targets.

## Fixed-tick execution and bounds

Polling queues at most one command per actor/tick. Sequence zero/replay is ignored. A same-tick flood discards the pending slot, coalesces one RateLimited result and acknowledges the latest rejected sequence; it cannot build a queue or spend multiple resources. Ownership, current state, known ability, cast busy, cooldown, mana, aim/range/LOS, observed future tick and effect capacity are checked before spending.

Movement runs before combat/casts. Area range/LOS is rechecked from current caster position at release; invalid release fizzles without a refund. Projectile releases from the current caster position, sweeps the entire segment, queries spatial candidates, respects walls and expires at range. Navigation LOS/projectile blocking conservatively uses the agent radius, not a separate physics simulation.

Dash is a fixed-speed movement phase, never a teleport. Shared straight clipping tests the whole prefix against inflated obstacles/bounds; it does not route around walls. Snapshots include destination/speed and the last processed ability sequence, even after a rejected command. Movement intentions cannot override the in-progress dash.

Latency compensation advances only the first projectile segment by server-measured RTT/2, capped by configured 100 ms (hard validation ceiling 200 ms). The added segment still tests collisions and total range. Stale/zero observations get no catch-up; future observations fail. No position/history rewind, extra attack, cooldown reduction, resource refund or AoE/dash rewind. High ping cannot enlarge the configured window.

Configuration defaults: maximum 128 active effects (hard 1024), lifetime budget 10 seconds (hard 60), impact 0.15 seconds. One reusable candidate set, bounded active-effect scans and connection-scoped AOI caches; no global idle-player combat scan or tick file IO. This prototype makes no production crowd-capacity claim. Larger effect counts/AOI workloads require profiling and effect spatial indexing.

## Protocol v6

Old clients receive a version-handshake rejection. Existing message IDs remain stable. New low-frequency reliable ordered messages include a ushort header:

| ID | Message | Body fields in order | Total bytes |
|---|---|---|---:|
| 19 | AbilityCommand | uint sequence/observedTick, ushort ability, float aim X/Z | 20 |
| 20 | AbilityResult | uint sequence/serverTick, byte outcome | 11 |
| 21 | AbilityLoadout | ulong owner, uint tick, double mana/maxMana, byte count; slots below | 31 + 47 × count |
| 22 | AbilityEffectState | ulong effect/actor, uint sequence/tick, ushort ability, byte form/phase, float origin/position/direction X/Z, float radius/speed/remaining | 66 |
| 23 | AbilityHit | ulong effect/actor/target, uint tick, double damage/targetHP | 46 |

Slot: ushort ID, byte form, float range/radius/speed, double cast/cooldown/cost/readyIn (47 bytes). Exact length, count, IDs, finite values, enums, phase/form combinations and nonnegative resource values are checked before use. Aim is a unit direction for projectile/dash and a world point for ground AoE.

Entity snapshot record becomes 44 bytes: existing ID/position/movement ack/target, then uint ability ack, float dash destination X/Z and speed. Snapshots remain Unreliable. The protocol cap is 24 records, with actual chunk capacity reduced to fit `peer.GetMaxSinglePacketSize(Unreliable)`; unreliable data never relies on fragmentation. Header is 7 bytes.

Resources/slots/results are owner-only. Effects are relevant only while actor and effect are in observer AOI. Reliable enter/phase-change/finish, including AOI exit/reentry, is separate from frequent snapshots. Hits require visible actor and target; target-only observers get health without revealing an unknown attacker.

## Client presentation

Local input predicts a bounded preview, not HP/damage/mana. Confirmation replaces the preview; rejection/2-second timeout clears it. Dash prediction executes the same shared geometry on physics ticks; reconciliation restores authoritative movement/dash state and replays an unacknowledged dash once at its original client tick. Predicted endpoints are not sent as ordinary movement intentions. Rejected prediction rolls back to the latest authoritative snapshot. Remote players retain interpolation.

World effect visuals display cast direction, area telegraph, projectile flight and dash segment. Visual travel cannot cause a hit. Each effect has reliable cleanup plus TTL, disconnect clears caches and previews. Effects are simple Mobile-renderer-compatible meshes, not final VFX. No `.tscn`, `project.godot`, generated UID or AGENTS edits.

## Verification

Build uses `.artifacts/stage6` without stopping existing user processes. NuGet audit is explicitly disabled for this isolated verification, not reported as a dependency-security check.

Executed build: `dotnet build Game.slnx --artifacts-path .artifacts/stage6 -m:1 -p:NuGetAudit=false` — 0 errors/warnings. `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage6 --no-build` outside the sandbox for local testhost/UDP — 159 passed. Server `--validate-content` passes schema/content/world/profile checks without opening UDP. `git diff --check` passed. Godot executable is unavailable in PATH: compilation is not visual acceptance, and two live Godot clients must still be checked by the user.

Coverage: casts and one-shot hits, cooldown/busy/replay/flood/ownership, malformed wire data and all truncations, mana exhaustion, walls/swept contact, dash clipping/restore, bounded latency, stale/future tick, effect capacity/expiry/disconnect/AOI lifecycle, startup validation and MTU-aware snapshot chunking. UDP integration checks identical projectile/AoE damage, dash endpoint/ack and private resources on two connections with 100–150 ms latency and 10% packet loss.

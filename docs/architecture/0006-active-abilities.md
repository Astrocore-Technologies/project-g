# ADR 0006: Prototype active abilities and authoritative dash

## Status and scope

Update (2026-10-10, avatar animation): current wire version is **31**. The skinned-avatar addendum below adds public cosmetic states and emotes. Gameplay ownership and balance are unchanged.

Update (2026-10-09, Swordsman): the implementation addendum at the end supersedes the historical v6 layouts and prototype combat restrictions below; the approved profession rules are in `../design/gameplay-contract.md`.

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

## Swordsman implementation addendum — 2026-10-09

This is content and behavior within the existing Client/Server/Shared boundaries, not a new engine/service/database architecture. The user's explicit threshold is **100 actual damage**, overriding the attached draft's 100 hits. All ten active skills, five passives and the extended existing dash are implemented; eight active slots remain the limit. Numbers are a tunable prototype, not a final balance claim.

### Ownership, state and limits

- `ServerWorld.SwordTraining` owns the trainer interaction, idempotent bound-sword grant, personal training receipt, proximity/LOS validation and voluntary profession offer. Damage is counted from authoritative ordinary attack results **before** equipment commands change that tick's loadout. Spells, Echo and another player's damage do not count. Reaching the threshold alone never offers or switches profession.
- The receipt is optional versioned `SavedProgression.SwordTraining` data. Older saves without it load unchanged; malformed/unsupported receipts fail explicitly. Item, progress, profession and learned skills use the existing atomic character checkpoint. No SQL schema migration or working-save rewrite is required. The city makes three regional writes possible, so checkpoint validation now uses the existing three-region limit instead of the former two-region limit.
- `AbilitySimulation.Melee` resolves spatially queried server-owned shapes, fixed aim, walls, physical armor, guarded impacts and statuses. Rend refreshes one bleed per source/target pair; the configured effect budget bounds bleeding. Only direct damage interrupts breathing; all accepted actions and movement intentions can cancel it without refund. Cast interruption honors the action's `interruptible` definition separately from stun immunity.
- `CombatSimulation.Swordsman` owns combo/parry/focus/footwork deadlines and equipped-sword checks. Passive effects and the long dash require the active profession and a usable equipped sword. Learned active skills remain available under the existing profession-retirement rules. Movement snapshots stay authoritative; ordinary movement multipliers do not extend dash distance.
- Three authored mannequins have 10,000 HP, no bleeding/stun, no character EXP, and a five-second reset after destruction. Skill practice is still allowed. These temporary training rules and the 8/s swordsman recovery override are recorded in the gameplay contract.
- No global idle-player scan, client damage authority, tick-time file I/O or hidden-profession catalog transfer was introduced. Active effects, bleeds, timed defense states and the bounded arena list are visited. Performance was functionally tested, not load-profiled for production crowds.

### Wire v30 (breaking v29)

IDs are unchanged. The handshake rejects mismatched versions; restart/rebuild **both server and client**. Sizes below include the two-byte message ID but exclude the existing region envelope.

| ID | Layout / change | Total bytes | Delivery / visibility |
|---|---|---:|---|
| 19 AbilityCommand | uint sequence, uint observed tick, ushort ability, float aim X/Z, appended float dash distance (0 = full range) | 24 | ReliableOrdered, intent from owner |
| 20 AbilityResult | Existing fields; adds NeedsSword / NeedsParry enum outcomes | 11 | ReliableOrdered, owner |
| 21 AbilityLoadout | ulong owner, uint tick, double mana/max mana, byte count; each slot: ushort ID, byte form, float range/radius/speed, double cast/cooldown/mana cost/ready-in, appended double stamina cost and byte availability | 31 + 56 × count (max 9) | ReliableOrdered, owner |
| 22 AbilityEffectState | Existing layout; allows Melee / Recovery forms with Telegraph / Impact, speed 0 | 66 | ReliableOrdered, AOI lifecycle |
| 23 AbilityHit | ulong effect/actor/target, uint tick, double damage/target HP, appended byte GuardImpact | 47 | ReliableOrdered, existing AOI visibility |
| 44 ProfessionState | ulong owner, uint tick, ushort active + bounded name, ushort offered + bounded name, appended double training damage/required | ≤188 | ReliableOrdered, owner; training shown only after accepting public exercise, removed from HUD on graduation |
| 85 DefenseState | Existing layout; movement multiplier now accepts 0..2 for roots/haste | 88 | Unreliable owner state; subsequent state repairs loss |
| 87 QuestCommand | uint sequence, byte action, ulong NPC; adds TrainingSword action 4 | 15 | ReliableOrdered, owner intent |
| 88 QuestReply | Existing uint sequence/tick, ulong NPC, byte outcome/choices, bounded speaker/text; choice bits 4 = grant sword, 8 = open profession confirmation | ≤720 | ReliableOrdered, owner |

Readers reject invalid lengths, trailing/truncated data, invalid enums and non-finite values. The server additionally validates ownership, received sequence/rate, knowledge, slot assignment, equipment, cooldown, stamina, parry window, distance and LOS. Dash distance is a bounded request, not an endpoint: the server caps it to the current profile and clips against its navigation. Movement snapshots and held-block messages retain their existing unreliable/sequenced delivery.

### Editing and player verification

- Balance/content: `Content.Server/Data/prototype.json`, especially `swordsman`, profession 2, `sword_*` abilities (20–29), `arena_training_sword` and `arena_dummy`. Localized explanatory text lives in `Content.Client/Scripts/UI/SwordsmanUi.cs`; update it when balance changes.
- Authored city: `Content.Client/Scenes/Regions/RiverCity/RiverCity.tscn`, `AuthoringAnchors/SwordTrainer`, `SwordTarget`, `SwordDummyWest`, `SwordDummyEast`. Visual-only reusable actor scenes: `Content.Client/Scenes/Actors/SwordTrainer.tscn` and `TrainingDummy.tscn`.
- Move the markers in Godot, then run `res://Tests/Regions/ExportRiverCity.tscn` to update server navigation/placements. Do **not** rerun the initial `BuildRiverCity` generator over a hand-edited scene. The export frees its instantiated scene and retains manual authoring. Current geometry hash remains `310137FDA1690781`.
- Player loop: approach Radan at the arena → F2 → receive sword → I, equip it → LMB mannequin until 100 damage → return and talk → review and explicitly confirm profession. P explains the profession/passives; K → Skills assigns any eight learned techniques. UI shows public training progress, stamina costs, missing sword and missing parry window. Models/VFX are editable primitive prototypes, not finished character art/animation.
- `AGENTS.md` now explicitly requires maintainable code/content, editable scenes and understandable player feedback.

### Executed checks for this addendum

- `dotnet build Game.slnx --no-restore -m:1 -p:OutputPath=D:/projects/project-g/.artifacts/swordsman-final/` — successful. Isolated output avoids the user's running server locks.
- `dotnet build Content.Client/Project-G.csproj --no-restore -m:1` — successful, 0 warnings/errors.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore -p:OutputPath=D:/projects/project-g/.artifacts/swordsman-final/ --logger 'console;verbosity=minimal'` — **518 passed, 7 skipped, 0 failed**. Skips are six opt-in PostgreSQL checks and one opt-in working-database-copy check; they were not executed.
- `dotnet .artifacts/swordsman-final/Content.Server.dll --validate-content` — schema 3, balance 3, 16 abilities, 4 creatures; successful.
- Domain/protocol coverage includes grant idempotency/full bag, wrong weapon/source/owner/distance, actual-damage threshold, return-before-offer, remote-confirm rejection, all ten techniques, five passives, blocks/parries, bleeding/stun immunity, channel interruption, resource/cooldown enforcement, private state round trips and invalid packets. SQLite coverage checks item/progress together and a three-region checkpoint.
- Godot Mobile/D3D12 `res://Tests/UI/SwordsmanSmoke.tscn` — **passed** with `--server-port=28952 --identity=sword-smoke-3 --capture` on an isolated server/database/profile: trainer, equip, LMB autoattack, voluntary confirmation, ten skills, long-dash profile, actual melee skill hit and durable reconnect. An earlier run exposed a fixed-delay race in the test; it now waits for server preparation before looking for the confirmation checkbox. Captured `.artifacts/swordsman-{trainer,arena,profession,skills,hud}.png` were visually inspected. The working character/save is untouched; the temporary server was stopped afterwards. No final-art acceptance or high-population load test is implied.
- `git -c core.safecrlf=false diff --check` — successful. Existing unrelated dirty files/imports are preserved.
- Existing dependency warnings remain: NU1903 for SQLitePCLRaw.lib.e_sqlite3 2.1.11 and NU1900 when the NuGet audit endpoint is unreachable. These runs are not a dependency-security certification.

## Sword animations and 20 m dash — 2026-10-09

At the user's request, the active Swordsman's dash range was increased to **20 m** (`swordsman.dashRange`); the ordinary non-Swordsman dash remains **3 m**, as explicitly reaffirmed by the user below. Cost 35, cooldown 7 s, current speed, short-to-cursor behavior, wall clipping and vulnerability remain unchanged. No wire change beyond v30 and no persistence migration.

The prototype capsule now has a visual sword rig with 13 editable clips: two alternating basic swings, nine offensive techniques, breathing and dash posture. `Content.Client/Scenes/Actors/SwordAttackRig.tscn` contains the weapon, pose carrier and AnimationPlayer; `Content.Client/Assets/Animations/Swordsman.tres` contains the keyframes. Edit those resources in Godot rather than rebuilding them in code. These are placeholder-body animations, not a finished humanoid skeleton or final weapon art.

`SwordAttackAnimation` samples normalized clips: time 0..0.35 is wind-up, server-confirmed contact starts recovery to 1. The remaining server cast time drives the wind-up; idle rigs do not process. Only the render mesh and weapon receive transforms, never the CharacterBody, collision, camera or authoritative position. Local prediction is reconciled into the same clip; duplicate/old confirmation, rejection, interrupted cast, timeout, death and actor removal do not leave a stuck pose. Remote players use the same existing public attack/effect events. No new packets or animation-driven damage are introduced. Bounded low-poly meshes/materials avoid lights, particles and physics in the animation rig; crowd GPU performance still needs profiling.

Verification for this change:

- `dotnet build Game.slnx --no-restore -m:1 -p:OutputPath=D:/projects/project-g/.artifacts/sword-animation/` and `dotnet build Content.Client/Project-G.csproj --no-restore -m:1` succeeded.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore -p:OutputPath=D:/projects/project-g/.artifacts/sword-animation/ --logger 'console;verbosity=minimal'`: **521 passed, 7 opt-in skips, 0 failed**. Adds 20 m movement/reconciliation, 20 m wall clipping and server over-range rejection/cost/cooldown coverage.
- `dotnet .artifacts/sword-animation/Content.Server.dll --validate-content` succeeded. Existing NuGet dependency/audit warnings above remain.
- Godot Mobile/D3D12 `res://Tests/Combat/SwordAnimationSmoke.tscn -- --capture`: all 13 clips, pose/reset, prediction confirmation/rejection, interruption, death and isolation from collision/actor movement. Screenshot `.artifacts/sword-animation-poses.png` was visually inspected.
- Godot `res://Tests/UI/SwordsmanSmoke.tscn -- --server-port=28953 --identity=sword-animation-1 --capture`: full trainer/equip/training/profession/reconnect loop passed and explicitly observed ordinary-attack and thrust animation from real network events. Separate database `.artifacts/sword-animation-live.db`; working player/save untouched. Test server stopped afterwards.
- `git -c core.safecrlf=false diff --check` passed. Real multiplayer latency/crowd animation profiling and final skeletal-art acceptance were not performed.

## Rejected ordinary-dash extension — 2026-10-09 (historical, reverted)

The user still observed a short dash across the sword ring. Read-only inspection confirmed their active profession was not Swordsman, so the earlier class-only override never applied: `abilities[test_dash].range` was still 3 m. The agent incorrectly interpreted this as permission to extend the base definition to 20 m. The user rejected that interpretation; this extension has been reverted. The following checks describe that historical implementation, not the current gameplay rule. No profession/equipment/save was changed.

This is a server content change; the existing owner loadout supplies its range to client prediction, HUD and authoritative command validation. Protocol remains v30, persistence is unchanged, and no production client loop or rendering work was added. Restart the server after rebuilding so it reloads the definition, then reconnect the client.

Regression coverage now checks actual 20 m fixed-tick travel, non-Swordsman and unequipped profiles, over-range rejection without spending, full cost/cooldown, short-to-cursor travel across the authored ring, practice only after arrival, and two lossy clients observing a complete 20 m dash. The boss area-escape test explicitly requests a short 3 m dodge: a full 20 m escape can leave aggro and cancel the telegraph, which is a different behavior from its locked-circle assertion.

Executed checks:

- `dotnet build Game.slnx --no-restore -m:1` succeeded; `dotnet build Content.Tests/Content.Tests.csproj --no-restore -m:1` succeeded after the boss test adjustment. Existing NU1903 SQLite dependency and NU1900 audit-endpoint warnings remain.
- `dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content` succeeded (schema 3, balance 3, 16 abilities, 4 creatures).
- Godot Mobile/D3D12 `res://Tests/Combat/DashDistanceSmoke.tscn -- --server-port=28954 --identity=dash-distance-1 --capture` passed against a temporary Development server using `.artifacts/dash-distance-live.db`. Real Space input and ground-projected cursor moved a fresh non-Swordsman from approximately `(19, -11)` to `(11, -11)`; measured authoritative travel **8.02 m**, within the cursor projection tolerance. Client reconciliation agreed. Before/after captures `.artifacts/dash-distance-{before,after}.png` were visually inspected. The isolated server was stopped; working player data was not modified.
- The first suite run exposed the boss-test distance assumption above, which was corrected. A subsequent run had one intermittent failure in the existing asynchronous PvP integration test `LossyDeathAndChanneledPickupPublishOnlyAfterAtomicCommit` (PK snapshot not received yet at its assertion); no PvP production or test behavior was changed for this task.
- Final repeat: `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --logger 'console;verbosity=minimal'` — **525 passed, 7 opt-in skips, 0 failed**. The intermittent PvP assertion remains a separate test-stability risk. The seven skipped PostgreSQL/working-database-copy checks were not run.
- `git -c core.safecrlf=false diff --check` passed. Existing unrelated changes and imports were preserved; the new test script's UID was generated by Godot.
- Crowd/mobile-device performance and live production-server behavior were not profiled; no such verification is implied by the desktop smoke check.

## Confirmed rule: ordinary 3 m, Swordsman 20 m — 2026-10-09

The user's explicit correction restores `abilities[test_dash].range` to **3 m**. Only the active Swordsman with a usable equipped sword receives the existing **20 m** override from `swordsman.dashRange`. A sword alone does not grant that range. Costs (20/35), cooldowns (3/7 s), cursor-distance cap and obstacle clipping are unchanged. No runtime client logic, wire layout, persistence, ownership or tick/rendering work changed; only server content, documentation and regression checks needed correction.

The ordinary profile tests reject both 3.01 m and 20 m requests without resource spending when profession or equipped sword requirements are absent. The Swordsman test still accepts 20 m and rejects 20.01 m. City tests verify the ordinary 3 m endpoint and reject an 8 m request. Baseline movement/progression/boss/UDP tests were restored to their original ordinary-dash distances.

Verification for the confirmed rule:

- `dotnet build Game.slnx --no-restore -m:1` — succeeded, no errors; existing NU1903 dependency and NU1900 audit warnings remain.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --logger 'console;verbosity=minimal'` — **525 passed, 7 opt-in skips, 0 failed**. Skipped PostgreSQL/working-database-copy checks were not executed.
- `dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content` — succeeded.
- Godot Mobile/D3D12 `res://Tests/Combat/DashDistanceSmoke.tscn -- --server-port=28955 --identity=dash-profession-only-1` — passed. Real Space input with the cursor 8 m away moved an ordinary character **3.00 m**, with matching client prediction and server position. A separate database `.artifacts/dash-profession-only-live.db` was used; working character data was untouched. The Swordsman distance was covered by server tests; its full Godot training loop was not rerun in this correction.
- `git -c core.safecrlf=false diff --check` — passed. Restart the rebuilt server and reconnect to load the corrected content.


## Skinned avatar presentation — 2026-10-10

The accepted animation plan adds one shared 40-bone humanoid library, 64 clips
(including the T rest pose), interchangeable head/hair modules and the ten existing
sword techniques. CharacterAnimator samples in-place poses and blends moving legs
under upper-body actions. SwordAttackAnimation retains sequence/effect de-duplication,
server windup/contact phases, rejection, interruption and timeout behavior. The
render skeleton never applies root motion to the authoritative navigation body.

New code lives under each project's `_NC/Animation` directory (client under
`Scripts/_NC/Animation`). Artist sources and generation scripts are under
`art-source/characters/male-base`; original static sources remain available.

### Protocol 31

Both endpoints must be rebuilt/restarted; the handshake rejects v30.
Packet lengths include the two-byte message ID, but not the region envelope.

| ID | Message and exact payload order | Bytes | Delivery |
|---|---|---|---|
| 90 | AvatarState: ulong entity, uint tick, byte flags (armed/blocking/focus), float facing X/Z, float parry/stun remaining, byte gesture, uint gesture sequence, float gesture age, uint rhythm cue serial | 44 | Unreliable AOI state, on entry and every second fixed tick |
| 91 | EmoteCommand: uint sequence, byte gesture (0 cancel, 1–8 social gestures) | 7 | ReliableOrdered owner intention |

No private stamina, stack count, profession conditions or balances enter the public
packet. The rhythm serial denotes an observable completed activation, not the internal
hit count. Late AOI entry initializes it without replaying past effects. All reads
reject wrong lengths, trailing bytes, invalid flags/enums/directions and nonfinite
or out-of-range durations. Server-side ownership, sequences, one queued request per
actor, rate limiting, alive/busy/movement state and cancellation are checked on the
fixed tick. Cosmetic requests share the existing save barrier; no new persistence
payload or database migration is introduced. Only active gestures are simulated.

Eight emotes are available through F4. Accepted trainer conversations, pickups and
resource/workbench operations trigger public gestures. Visible block, parry, stun,
equipped sword and passive cues are derived from server state. Sounds and blade
trails are local, bounded, spatial and do not determine a hit. Character audio is
an original synthesized first pass; recorded Foley can replace its WAV files.

Verification entry points: AvatarProtocolTests, AvatarSimulationTests, existing
Swordsman/Defense tests, CharacterAnimationGallery (--smoke, optional --capture),
AvatarLiveSmoke (isolated server, two clients), SwordAnimationSmoke and UiScreensSmoke.
No crowd-load or packaged-platform export performance claim is made.

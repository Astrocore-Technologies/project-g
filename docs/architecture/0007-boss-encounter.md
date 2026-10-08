# Checkpoint 7.2: Fixed-stat prototype boss

## Status and boundaries

7.1 accepted; 7.2 implemented, awaiting user acceptance. No foundation, persistence or authoritative ownership change. Ordinary monster and dummy remain. No loot/contribution/EXP, threat table, regen, automatic respawn, scaling or encounter-reset rule is introduced.

Server owns targeting, alternation, locked geometry, deadlines, damage and HP. Shared contains revealed area DTO/phase only. Client interpolates NPCs and renders server-confirmed telegraphs/health, never chooses a boss attack or predicts hits.

## Data and simulation

Existing immutable schema 2 gains `test_boss`, `boss_claw` and `boss_ground_area`; no data-schema or database migration. Boss base HP 220 and VIT 10 give 242 HP. STR 8 / INT 5 / DEX 5 affect the existing formulas. Claw range 2.5, weapon attack 12, base interval 1.5 seconds. Area: power 12 + MATK × 0.5, radius 2, range 8, base cast 1.5 seconds, cooldown 3, zero resource cost. These are temporary data-driven balance, not permanent boss design. Player count is never a formula input.

`BossOptions.Actor` configures the separate actor at (7,-8), speed 2.5, aggro 5, leash 8, decision interval 0.2 and directional windup 1.2. Area reference must belong to the selected creature. Definition references, supported form/zero cost, float ranges, spatial-query budget, effective cast [0.2,10] and impact lifetime validate before UDP startup. Disabled bosses are not instantiated; test arenas opt in explicitly.

The existing NpcSimulation owns each of the two bounded actors independently. Boss alternates cone then area, preserving the sequence/deadline checks. Shared global action cooldown begins on release; area uses its configured cooldown and melee its weapon-derived interval. Direction or target-position center locks on start. Both actors move before player attacks/spells; boss release resolves afterward, allowing a killing blow to cancel windup. There is no tracking circle or historical-position rewind.

Area release rechecks caster-to-center range/LOS, spatially queries players, filters live player kind and center-to-player LOS, applies signed MDEF and caps damage to remaining HP. Each candidate is hit once; joining/leaving the circle changes eligibility at release. No NPC or friendly-player damage path is added. A repeated sequence cannot reapply the attack. Missed area still consumes the release cooldown. Death, lost/dead/disconnected target or leash cancels the cast. Returning home does not heal or reset progression/pattern.

At most two active NPC instances in this prototype. Perception/repath remain lower-frequency spatial queries; no global idle-player scan. Tick does no IO. Larger crowds need activation/scheduling and profiling, not an inferred production capacity claim.

## Protocol v8

`CombatEntityKind.Boss = 3` reveals presentation role. Older peers fail handshake; existing message IDs/layouts remain stable. NPC movement stays in MTU-aware Unreliable snapshots, without fake PlayerIds.

New **25 / NpcArea**, ReliableOrdered, **35 total bytes**: ushort type, ulong actor, uint sequence/serverTick, float center X/Z, float radius/remaining seconds, byte phase (Telegraph/Impact/Finished). Body exactly 33 bytes; finite values, IDs, enums, positive radius, remaining [0,10], Finished=0, truncations and trailing bytes are validated. Low-frequency phase/version changes and current state on actor AOI entry are reliable; remaining time is not sent reliably each tick. Finished cancels/cleans up; it does not deal damage.

Confirmed per-target area hits reuse AttackEvent (17), with Origin=center, Range=radius, Direction=UnitY, shared cast sequence and Critical=false. Multiple targets can therefore have the same actor/sequence; clients apply each target's confirmed HP but do not replay the caster visual. Area phase handles the visual impact separately. Target-only observers receive CombatState health without an unseen attacker. No attack list/AI target IDs/stat formulas are transmitted.

## Client / acceptance

Boss uses the same bounded NPC interpolation as the monster. Larger orange cylinder and BOSS HP label distinguish it. Locked orange ground circle becomes red on confirmed impact; cone windup stays red. The area mesh is actor-owned but positioned in world space, with phase/TTL/death/AOI cleanup. Area hits do not incorrectly display a melee cone. Existing RMB/LMB/Q/W/Space remain. No scene/project/UID/AGENTS edits.

Manual acceptance: restart server/two clients v8, approach (7,-8), dodge both telegraphs, move out/into the locked area, defeat solo or cooperatively and check identical health/events under simulated network conditions. No automatic respawn; existing test-session/arena restart limitations remain. Stage 8 begins only after acceptance.

## Executed verification

- `dotnet build Game.slnx --artifacts-path .artifacts/stage7-2 -m:1 -p:NuGetAudit=false`: 0 errors/warnings. Audit explicitly disabled, not a dependency-security assessment.
- `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage7-2 --no-build` outside the sandbox for testhost/UDP: 185 passed.
- Built server `--validate-content`: successful schema 2 / 4 weapons / 4 abilities / 3 creatures and enabled actor/profile validation without UDP startup.
- `git diff --check`: no whitespace errors. User processes were not stopped.
- Godot executable absent from PATH: scene not launched; compilation/UDP tests do not replace visual acceptance.

Coverage includes alternation/deadlines, locked center, no pre-release damage, dodge, multi-player one-shot damage/replay, LOS, kill/disconnect cancellation, startup validation, fixed-health solo/cooperative victory, exact wire parsing and two-client matching telegraphs/area hits with 100–150 ms latency and 10% loss. Previous movement/monster/ability tests remain green.

# ADR 0008: Character persistence and Development identity

## Status

Accepted architecture by the user; implementation awaits stage 8 acceptance.
User approved SQLite for the executable server during development (clarifying the earlier tests-only wording).
PostgreSQL from ADR 0001 remains the planned long-term database; Development uses SQLite now.

## Context

Reconnect must restore a character, not create a new runtime avatar with fresh resources.
Runtime PlayerId/NetworkEntityId and command sequences cannot serve as database identity.
Authentication for Steam/Production is not implemented yet.

## Decision

- A first localhost Development login receives a server-generated 256-bit random token. Database stores SHA-256 of decoded token bytes, never the credential itself. Unknown tokens fail closed; no replacement character is silently created.
- One character per Development identity is a temporary test assumption, not the permanent account/character-selection model. Persistent CharacterId is a UUID; it is not an entity/player network ID.
- Client stores the token atomically in `user://development-identities/<profile>.token`. Default profile is `default`; `--identity=alice` / `--identity=bob` selects independent local characters. Do not share, commit or print these files.
- Executable server requires Development. Its default provider is SQLite at `.data/project-g-development.db`, relative to the launch directory; launch from the repository root. The file is independent of build artifacts and ignored by Git. Explicit `Postgres` provider requires credentials from configuration/environment. No automatic provider/ephemeral fallback. Remote IPs cannot use Development identity. This is not secure Production authentication: no encrypted transport, account recovery or Steam proof.
- Model v1 stores region/profile IDs, base stats, position, HP, mana, melee cooldown and stable-ID ability cooldowns. Definitions, formulas, secrets, runtime IDs and prediction sequences stay out of saved mutable state.
- Each provider has a schema version table + embedded additive migration 0001, applied transactionally before opening UDP. Future/invalid models fail closed. Content/geometry incompatibility requires an explicit repair/migration, not teleport/heal/reset.
- SQLite uses WAL + FULL synchronous commits and an exclusive per-character file handle held through final save; process death releases ownership even if the lock file remains. SQLite synchronous I/O runs on workers, not simulation/polling. PostgreSQL holds a dedicated session advisory lock. Both use fresh owner UUID + revision fencing on login and compare-and-swap on saves. A conflict aborts the entire tick transaction.
- Changed players are selected from movement/action/damage dirty sets; idle characters are not scanned or saved every tick. All changed characters from one tick commit atomically before its snapshots and combat/resource events are published.
- While a commit is pending, networking continues polling, next simulation tick waits, intentions are bounded to the latest movement and first attack/ability per connection. A store failure stops simulation without publishing uncommitted results.
- Login/checkpoint/final save have 10-second operation deadlines; PostgreSQL connect/commands have 5-second limits. Shutdown drains in-flight checkpoint, active characters and late logins, then releases ownership.
- Development session budget defaults to 32 (configurable 1..64). This slice is not proof of the 200/400 regional load budgets. Per-tick durable writes, SQLite's single writer and PostgreSQL dedicated ownership connections need measurement before scaling; later durable checkpoint optimization requires another ADR.

## Alternatives considered

1. Steam authentication now: appropriate for PC Production, but adds Steam deployment/ticket validation to this persistence slice. Deferred by user approval of Development tokens.
2. Development tokens now: small local workflow, easy reconnect tests; credentials are bearer secrets and cannot safely authorize Internet traffic. Selected and environment/IP restricted.
3. Periodic asynchronous autosaves: cheaper than a durability barrier, but a crash can roll back already published HP/mana/position. Not selected for the acceptance criterion.
4. SQLite as Development server storage: simpler local setup without Docker/database service, but single-writer/file ownership is not a Production distribution architecture. Selected explicitly by the user; PostgreSQL is retained for the later rollout. Tests exercise the same SQLite implementation as the executable.

## Consequences

- Protocol v9 adds token string after ClientHello(version, build) and after ServerWelcome(player ID, tick rate, Unix time). Empty or exactly 64 uppercase hex characters. ReliableOrdered; no token in frequent snapshots or AOI state. Old hello layout receives UnsupportedProtocol, never a successful v9 login.
- Each character-changing tick is durable before confirmation. Prediction is still speculative and may reconcile. NPC/world state is not persistent in this stage; NPC encounter state resets on server restart.
- In-progress casts/effects/routes are not resumed. Already paid mana/cooldown remains spent; restored motion is idle at the saved position.
- Temporary offline rule: wall-clock offline time advances cooldowns, but does not heal, replenish mana or respawn. HP=0 remains HP=0. No permanent death rule is introduced.
- Token loss does not provide account recovery. First-login failure before token receipt can leave an inaccessible Development row; automatic deletion/recovery is intentionally absent.
- PostgreSQL migration/advisory locks remain provider-specific. Passing SQLite tests does not verify that provider. Moving Development characters to PostgreSQL later needs an explicit export/import preserving UUID/hash/state and a controlled cutover; changing provider alone does not copy the rows.

## Migration / rollout

No previous persistent data exists. Bootstrap only adds Project G tables; no destructive migration.
Restart server and clients together on v9. Development creates its SQLite file automatically; see `docs/development/character-persistence.md`.

## Deferred

Production/Steam identity, account recovery, character selection, inventory/progression/profession/world persistence,
respawn, schema upgrades beyond v1, live PostgreSQL integration verification, and durable-write load testing.

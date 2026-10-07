# ADR 0003: Spatial interest and bounded snapshot chunks

## Status

Implemented for development stage 2; awaiting user acceptance.

## Decisions

- ServerWorld owns runtime entities, connection ownership and the spatial index. Position mutations update the index on the fixed simulation tick. Movement only visits active movers, not every idle entity.
- Runtime entity IDs are never reused. ServerPlayer is a separate data type; transport does not mutate positions.
- A uniform grid indexes entities by X/Z. Radius queries visit intersecting cells and filter by exact squared distance. Empty cells are removed.
- Each accepted connection has one reusable InterestView. It produces entered/left IDs and relevant snapshot state. The observer always sees its own entity.
- Development configuration: cell size 8, enter radius 12, exit radius 14. Hysteresis prevents repeated spawn/despawn near the boundary. These are tunable test-region values, not permanent gameplay vision rules.
- Spawn/despawn are ReliableOrdered on the same stream. Leaving AOI does not destroy the authoritative entity; reentry uses the same ID. Disconnect destroys that session's runtime entity and releases its view.
- No global spawn broadcast or full-world snapshot is sent to a new client.
- There is no 32-entity AOI cap. One observer can receive multiple independent snapshot chunks per tick. Each chunk has at most 32 entities (647 payload bytes), below the 1200-byte application packet budget.
- Snapshot chunks use Unreliable, not Sequenced: one chunk must not invalidate another chunk for the same tick. No atomic reassembly is needed; each entity accepts only newer ticks. Loss is repaired by subsequent snapshots.
- Buffers and per-observer collections are reused on the server. Queries and serialization avoid hot-path LINQ and temporary arrays.

## Protocol version 3

Message IDs remain unchanged. A version-2 client is rejected by the versioned handshake.

- PlayerSpawn (10), ReliableOrdered: ushort type, ulong player ID, ulong entity ID, float X, float Z, six movement floats, **uint server tick**.
- PlayerDespawn (11), ReliableOrdered: ushort type, ulong entity ID.
- MoveCommand (12), Sequenced: unchanged sequence, client tick and destination intention.
- WorldSnapshot (13), Unreliable: ushort type, uint server tick, byte count, then count entries of ulong entity ID, float X, float Z, uint processed sequence.

Spawn tick establishes the client's baseline for this AOI visit. Packets older than or equal to that baseline are ignored, including delayed snapshots from an earlier visit. Snapshots for unknown entities are ignored until reliable spawn arrives; future ticks repair the missing state.

## Boundaries and deferred work

- Server stays independent of Godot; Shared contains only public wire contracts and deterministic movement helpers.
- Client movement input follows gameplay contract: RMB; LMB is reserved for manual attacks.
- No persistence migration or new service/process boundary.
- This stage indexes player entities in the existing flat test region. NPCs, obstacles, navigation, region handoff and distributed simulation are later stages.
- No promise of production capacity: dense AOI still costs work proportional to relevant entities. Load budgets and massive single-location events require later profiling.

## Verification

- Domain tests: exact radius, negative grid coordinates, relocation, empty-cell cleanup, hysteresis, lifecycle and 65 relevant entities.
- Protocol tests: spawn tick round-trip, chunks exceeding 32 total entities, packet bounds, truncation, trailing bytes and invalid IDs/counts.
- Real UDP tests: two clients at 100–150 ms simulated receive latency and 10% packet loss; disconnect/reconnect; AOI exit/reentry; 65 entities received across chunks of the same tick.
- Godot visual acceptance remains a user check when the executable is unavailable to the agent.

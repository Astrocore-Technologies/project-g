# ADR 0004: Authoritative prototype navigation and obstacles

## Status

Implemented for development stage 3; awaiting user acceptance.

## Scope

A static, flat X/Z test region. This is a bounded prototype, not the final navigation architecture for large 3D regions. No Godot dependency is added to Server or Shared, no persistence migration, service boundary or authority change.

## Decisions

- Server configuration defines cell size, agent radius and blocking rectangles. Movement bounds define the grid origin and dimensions; dimensions must fit whole cells, with at most 1024 cells.
- Default public test geometry: 30 by 30 one-unit cells, radius 0.45, central wall X [-1, 1], Z [-5, 5]. Actor diameter must be smaller than a cell for this prototype's center-node graph.
- Invalid geometry or a map without safe spawn fails on load. Spawn placement finds a safe point if the test spawn lies inside a wall.
- NavigationGrid copies input geometry and remains immutable. It contains only public collision data, no hidden content definitions or world secrets.
- Deterministic, four-neighbour A* uses stable ID tie-breaking, reusable bounded scratch buffers and radius-aware line-of-sight smoothing. Diagonal shortcuts are allowed only after full collision validation.
- Swept movement checks radius-expanded blocked-cell boxes, conservatively including corners and the map boundary. Testing the whole segment prevents tunneling through a thin wall.
- Route following spends one speed budget across all waypoints of a fixed tick. A corner does not increase speed. Identical destinations reuse the existing route.
- Changed destinations can request at most one path search per player per server tick, including failed searches. Invalid/unreachable commands do not mutate position, target or acknowledged sequence. Later repeated intentions can retry after the next tick.
- ServerWorld owns authoritative NavigationMover instances and shares one sequential pathfinding workspace. Domain simulation remains independent of UDP/Godot.
- Prediction uses the same geometry and route rules. Reconciliation restores authoritative position and destination, then replays pending intentions. It replans from the corrected position rather than transmitting complete routes.
- Client obstacle visuals and physics shapes derive from server geometry; there is no second manually maintained wall layout. Avatar radius uses the same data.
- Local visual correction does not ease through a blocked segment. Remote interpolation holds the preceding point if a straight interpolation segment would cut a blocked corner.

## Protocol version 4

Existing IDs remain unchanged; new message RegionNavigation is ID 14. Version-3 clients fail the existing versioned handshake.

- Connection order on ReliableOrdered: ServerWelcome, RegionNavigation, relevant PlayerSpawn messages.
- RegionNavigation (14): ushort type, float origin X/Z, float cell size, float radius, ushort width/height, one byte per cell (0 walkable, 1 blocked). Maximum 1046 payload bytes. Dimensions, finite values, cell values and exact body length are validated before use.
- WorldSnapshot (13), Unreliable: each entry now contains ulong entity ID, float position X/Z, uint acknowledged sequence, **float destination X/Z**. At 32 entries the packet is 903 bytes, below the 1200-byte limit. Partial chunks still use per-entity tick filtering.
- MoveCommand is unchanged: destination intention, never claimed position or a client-provided route.

## Verification and limitations

- Tests cover wall detours, unreachable/blocked targets, radius/corner/boundary collision, long-tick traversal, speed limits, deterministic prediction, reconciliation replay, spawn safety and per-tick path-search limits.
- Wire tests cover round-trip, packet budgets, invalid dimensions/cells, NaN, truncation and trailing bytes.
- UDP integration verifies geometry-before-spawn and wall detours at simulated 100–150 ms receive latency and 10% packet loss; existing AOI and reconnect tests remain.
- A warm-path allocation test checks repeated destinations and route following. No production capacity claim is made.
- Deferred: dynamic obstacles/map versions, elevations, ramps, multi-level geometry, navigation chunk streaming, region handoff and a production navigation bake/pipeline.
- Sending the complete public prototype map is not a policy for unrevealed regions: larger/secret areas will require relevant public-geometry streaming.
- Godot visual acceptance must be checked separately when its executable is unavailable to the agent.

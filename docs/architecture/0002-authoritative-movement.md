# ADR 0002: Authoritative network movement

## Status

Accepted by the user for development stage 1.

The decisions below describe protocol version 2. Stage 2 changes to interest management and snapshots are documented in ADR 0003.

## Decisions

- Protocol version 2 adds reliable spawn/despawn messages, sequenced movement commands, and sequenced world snapshots.
- A movement command contains only sequence, client tick, and target X/Z. It never contains a claimed player position.
- The server owns entity IDs, validates command order and bounds, and advances movement at 20 Hz.
- Shared movement math is independent of Godot. Clients use it for prediction; server snapshots always win reconciliation.
- Local movement replays unacknowledged commands after correction. Remote players render from a 100 ms interpolation buffer.
- Disconnect removes the runtime entity. Reconnect creates a new session until persistent accounts are implemented.
- Stage 1 uses a single flat test region with bounded movement and at most 32 entities per snapshot.

## Deferred

- Server-side navigation and obstacle collision.
- Spatial interest management and snapshot partitioning.
- Persistent reconnect and character restoration.
- Region transfers.

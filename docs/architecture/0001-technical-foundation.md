# ADR 0001: Technical foundation

## Status

Accepted for development stage 0.

## Decisions

- The authoritative server simulation uses a fixed 20 Hz tick.
- Runtime network entities use opaque server-issued 64-bit identifiers. Zero is invalid, and IDs are not reused during a server process lifetime.
- Persistent database IDs are separate from runtime network IDs.
- The binary LiteNetLib protocol starts at version 1. Message types use unsigned 16-bit identifiers.
- A client must complete `ClientHello -> ServerWelcome` before receiving a player ID. Invalid clients receive a structured `ServerReject`.
- A normal region targets 200 active players and must tolerate short bursts up to 400. These figures are architecture budgets until load tests prove them.
- The world is logically divided into regions. Regions initially run in one modular server process and retain boundaries that allow later process separation.
- PostgreSQL is the planned source of truth. Persistence will use versioned models and migrations; economic writes must be transactional and auditable.
- Configuration is layered through `appsettings.json`, environment-specific files, environment variables, and command-line overrides. Secrets never belong in tracked files.
- Server diagnostics use structured `Microsoft.Extensions.Logging` fields.
- Shared protocol and server handshake behavior are covered by separate xUnit test projects.

## Deferred

- Database schema and persistence implementation.
- Region simulation and process distribution.
- Movement, snapshots, prediction, reconciliation, and interest management.
- Combat and content systems.

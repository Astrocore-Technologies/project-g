# ADR 0011: Regional travel in the vertical slice

## Status

Accepted by the user on 2026-10-08. One-process travel slice implemented;
manual acceptance pending. Mounts and map sharing explicitly deferred by the user.

## Context

Stage 18 needs two adjacent territories without layers or fast travel. ADR 0001
already permits multiple regions in one process. SQLite remains Development storage.
Before this change the executable owned one ServerWorld; creating a second independent social
registry or releasing the character database lease during travel is not safe.

## Decision

- One process, one transport and fixed-tick coordinator; two regional simulations.
- One process-wide runtime entity allocator and one character ownership registry.
- One realm-wide SocialSimulation; membership survives travel. HP/positions remain
  private to party members in the same region. Travel is not logout.
- Clients send movement intentions, never a destination-region position. The server
  detects a configured adjacent boundary and selects the arrival point.
- Freeze the source actor, reserve an inactive destination actor, then persist the
  complete character, inventory, progression and Echo state under the existing lease
  and revision fence. Publish and activate only after successful commit.
- A reservation may be cancelled only before a database write has started. The host
  fails closed on write/activation errors and requires restart from durable state.
  After commit, failures recover the committed destination, never reactivate the
  old source. An uncertain database outcome must not trigger a final autosave.
- Transfer operations carry an operation ID and ownership epoch. Pending or stale
  commands cannot mutate the frozen character; delayed old-region packets are ignored.
- Persistent region identifiers and geometry are validated; existing `prototype`
  saves remain valid. Unknown/incompatible regions fail closed, never reset.
- Client loads only the current region. Reliable transition control; ordinary
  snapshots remain unreliable and carry region/epoch relevance.
- Private explored maps survive round trips; no automatic party fog reveal.
  Mounts and cartography sharing are not part of this implementation.

## Alternatives considered

Separate processes now would exercise real inter-process handoff and isolate region
failures, but require routing, authenticated server messages, distributed fencing,
partial-failure recovery and additional local services. It also conflicts with the
current single-authority social implementation unless routed through a coordinator.

Selected one-process regional simulations: simpler operation, lower transition
latency and deterministic tests; one process crash affects all regions. Future process
separation will require a separate transport/recovery ADR, not just deployment flags.
Both approaches can keep the mobile client limited to one loaded territory.

## Consequences

Region keys are persistent compatibility concerns. Epochs are process-wide monotonic
session fences; restarting the process invalidates the old network sessions.
Protocol24 uses reliable RegionEnter (81) and epoch-scoped RegionPacket (82);
snapshot delivery remains unreliable. The client discards stale/future epochs.
World-node/market/loot state must stay with its region; character possessions travel.
One process is not proof of production distribution or the 200/400-player budgets.

## Migration / rollout

Additive compatible state changes only; no database reset. Keep the existing character
lease for the whole session. Restart client and server together when the wire version
changes. Test on isolated databases, never modify the user's running world.

## Deferred

Inter-process handoff, gateways, distributed social services, production authentication,
ships, mounts, map sharing/sales and new Notion design refinements.

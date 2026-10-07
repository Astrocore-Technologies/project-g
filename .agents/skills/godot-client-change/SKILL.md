---
name: godot-client-change
description: Изменения Godot scenes, input, camera, UI, rendering, prediction/interpolation/reconciliation и client-side network presentation в Project G.
---

# Godot Client Change

## Client boundaries
Client может читать input, predict presentation, interpolate remote entities, animate/render/UI и показывать server-confirmed state.

Client не решает authoritative position/damage/loot/progression/inventory/profession/economy/world outcomes.

## Current PC input contract
- RMB ground = click-to-move.
- LMB = manual basic attack.
- Q/W/E/R + A/S/D/F = skills.
- Space = tactical Dodge/Dash.

Не возвращай LMB movement или auto-basic-attack.

## Godot rules
- `_PhysicsProcess` для movement/prediction.
- Visual interpolation/UI отделять от authority.
- Cache node refs.
- Validate required `[Export]` refs.
- Subscribe/unsubscribe symmetrically.
- Network stops cleanly in `_ExitTree`.
- No per-frame string node lookups.
- Minimal `.tscn` edits.
- Generated `.uid` не редактировать вручную без необходимости.

## Mobile constraints
Сохраняй high-angle readability, touch-mappable semantics и разумные draw calls/effects даже при PC-first.

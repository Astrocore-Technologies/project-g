# UI rework concepts — 2026-10-17

Created with the built-in `image_gen` tool for the user's requested Project G UI redesign concepts. This directory contains concept illustrations, not runtime sprites, working UI screenshots, approved gameplay data or a replacement character model.

Design and implementation plan: `docs/design/ui-rework-2026-10-17.md`.

The user approved these four images as the required UI visual target on 2026-10-17: «вот именно такой юи я от тебя требую». Match their composition, spacing, materials, typography hierarchy and art quality in actual Godot scene renders. Approval covers the UI direction; example content and numbers remain illustrative.

| Selected workspace file | Generated source filename |
| --- | --- |
| `01-hud.png` | `exec-80b957cb-781a-43d5-b5c9-dffff53ecd84.png` |
| `02-character.png` | `exec-670e89c2-73b4-4bcb-a2d6-28faf8e3b5b6.png` |
| `03-inventory.png` | `exec-349c1770-3da9-404e-958b-9376c370f5e5.png` |
| `04-other-screens.png` | `exec-12cbf59a-9bf1-4f94-8773-594b07adfe13.png` |

All source files were returned under `C:/Users/Yuri/.codex/generated_images/01a14a84-aeca-7f53-a1aa-2d512acd67a0/`. Selected images are copied into this repository so the plan does not depend on those originals. Generation prompts are in `prompts.json`; targeted correction prompts are in `edits.json`. Initial drafts remain at their original generated locations.

Reviewed: required HUD panels removed in concept; minimap arrow centered within visible terrain; navigation shortcuts corrected to I/K/L/G/M/Esc; separate character/inventory compositions; four-window family; neutral empty Echo collection without invented acquisition. Inventory capacity corrected to the 18 occupied illustrated slots. Small generated text artifacts, including the inventory footer split-stack hint, are not final localized UI text. Runtime labels will be editable controls with verified bindings and actual state.

No code or resources from third-party art packages were copied for these generated concepts. This provenance note is not a new repository license grant. Geography, character art, items, rewards and numeric examples remain illustrative.

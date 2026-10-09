# UI foundation / art handoff

User direction (2026-10-09): prepare UI infrastructure; source assets supplied in Project_G_UI_Source_Kit. Its palette, component SVGs and 40 white icons are integrated through tools/import-ui-source-kit.py. Generated character illustrations are not assigned as player appearances.

## Scope
Client-only theme resources, keyed art catalog, reusable controls, eight live screen compositions and an offline art workbench. Presenters bind the source-kit theme to existing authoritative state and commands without inventing gameplay state. Server commands and authoritative ownership remain unchanged. No protocol or database migration.

## Boundaries
UI resources contain only public presentation assets. Runtime network handles, localized names and slot numbers are not stable character/item appearance keys. Skills already have stable public IDs. Inventory/Echo/NPC-specific art awaits a suitable public content ID or appearance contract; category/neutral fallbacks remain in the meantime.

## Performance
Cache theme/catalog once, update screen contents on state events, keep model preview in an isolated viewport rendered only when visible. Screen containers scale from 1280x720; original art uses nine-patch styles or aspect-preserving textures. No per-frame catalog scans or image loads.

## Verification plan
Build Game.slnx. Run actual Godot workbench/smoke for catalog errors, neutral fallbacks, screen slots, resize, focus/modal lifecycle and isolated model preview. Exercise existing character and quest smoke checks against a dedicated temporary server/database. Record actual results in docs/development/ui-assets.md.

## Deferred
The eight live compositions are integrated through UiComposition, UiWindow and GameNavigation. WorldController registers routes after presenters exist and stops movement/autoattack on ModalOpened. Route and event teardown follows the world lifecycle. Opening one window closes the previous one through its close policy, preserving profession cancellation semantics. Gameplay systems absent today (chat, Adventurer Guild ranks/contracts, Echo affinity, biographies) are not activated by scaffolds. No generated characters, fake map geography, fabricated values, or new gameplay rules.


## Runtime screens
The workbench scenes remain presentation-only layout guides. Actual controls live in PlayerHud, InventoryPresentation, ProgressionPresentation, StarterZonePresentation, QuestPresentation, EchoControls and GameNavigation. StarterMap draws only explored navigation cells; it never loads secret places or accepts pins outside explored cells. All received roster, inventory and progression state remains owner-filtered. Existing server confirmations, quote expiry and immutable profession acknowledgement are retained. A bounded notification feed uses received world/quest/social context, not a simulated chat.

UiScreensSmoke opens every live route against an isolated server, verifies one modal, screen bounds, gameplay-key lock, map fog/pins, inventory search, two resolutions, Escape and teardown. The gameplay smoke aims into unobstructed world space; the HUD intentionally consumes mouse input over its own panels.

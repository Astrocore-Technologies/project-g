# Linvel water and painted shading

Approved direction: Stylized Hand-Painted Anime 3D. Calm clear harbor, broader
open-sea motion, painted foam, soft light and consistent material response.

Scope: client shader/material resources, saved coast scene and a native preview.
No gameplay, protocol, persistence, collision or server changes. Existing district
scenes and hand placements remain authoritative. No runtime scene generator.

Implementation: shared soft light include for city painted props and limestone;
one continuous ocean with localized harbor shelter, authored shoreline field,
depth coloration/visibility and painted foam. Water parameters remain editable
in external materials. Existing forest assets are reused read-only.

Risks: transparent water fill rate, depth feedback, specular aliasing, shoreline
halos and visible sea mesh transitions. Verify in the actual Mobile renderer.

Verification: capture identical coast and city views before/after, inspect near
piers, quay, open sea, shallows and high camera; capture time-separated motion;
check shader compilation/editor logs and city structural regression; build solution.

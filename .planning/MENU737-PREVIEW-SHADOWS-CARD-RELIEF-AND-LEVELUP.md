# Build737: preview shadows, stronger card relief and native level-up selection

## Requested behavior and evidence

The maintainer confirms Build736 fixes the pitched character window, native text
flicker and main-menu hand trailing. This is focused hardware acceptance of those
reported paths, not a claim about all devices or every optional backend probe.

The next report requests shadowless character previews inside windows, stronger
relief for ordinary and item cards, and restoration of the right-side level-up
selection list after the two new cards are revealed. Preserve the flat game's
ability to browse the same list outside an active level-up if it supports that.

Six immutable copies under ignored `debug/menu737/hardware-inputs`, with hashes
in `round-start.json`, pin the new local Build736 logs and stale remote Build717
logs. No new screenshot/video was supplied. The local log reports seating the
native level-up inventory, then control coordinates near +/-2147483648 and a
fixed-fit group width of approximately 4294968000 pixels. Those measurements
explain why the original list exists but is outside the visible window.

## Implementation boundaries

Three disjoint worker worktrees start from published dev `b96d15578`: preview
shadow suppression, raised-card depth constants, and the native level-up
composition. The integrator owns the stamp, independent desktop reference planes,
integration evidence and final documentation. No game data, actor/network state,
tracking, HDR/MSAA, capture rate or sampling quality changes are authorized here.

The original forward GUI 3D camera has preview-owned lights, including shadowed
directional/point lights. Suppress shadow contribution only during the private
preview render and restore exact native state afterward, including exceptional
exit. Retain original lighting color, materials, model geometry and world shadows.

Raise all existing card classes proportionally by 60 percent: mini ability cards
from .035 to .056 of physical width; full ability and item cards from .065 to
.104. Retain the complete native subtree, physical-width/XY fitting, original
hover/selection behavior, coplanar-label separation and restoration. Existing
independent desktop reference planes use the requested new depths; their pixel
and clipping tolerances remain unchanged.

The original TMP implementation initializes empty ink extents to +/-2147483648
and only accumulates visible characters. Active whitespace-only and not-yet-built
labels therefore return negative-size bounds. The composite must include only
finite positive ink bounds in its retained preview envelope, while preserving
the actual native card/text rect and visible overflowing glyphs.

Native `UIResetLevelUpWindow.Show` enables its original display toggle;
`ToggleDisplay(true)` calls `inventory.Show(... locked: true, isOpenByToggle: true)`
and `EnableInteraction`. Outside leveling this supports browsing and hovering,
not free card acquisition. Reset permissions remain native (including campaign
and level restrictions). Keep that same original control and inventory usable.

## Validation plan

Implementation class B: three bounded presentation repairs, no general compositor
or native authority changes. The complete checkpoint cadence is separately due:
eleven source-changing first-parent commits follow the last complete invocation
at `f02f033a2`. Execute the integrated complete local gate once, including the
actual golden-vector executable, all source gates, strict Debug/Release builds,
bundle/surface checks and compiled scope. Do not duplicate an affected-union run
before that checkpoint. Workers execute their owned focused regressions and
causal controls; subsequent bounded repairs rerun only affected coverage.

Keep every failed attempt, source/input binding, assertion/control count and
cleanup receipt. Remove generated project caches after each run using the central
runner or `cleanup_completed_run`, then archive/verify/retire the three owned
worktrees. Native D3D11/headset appearance remains a separate acceptance step.

Final integration, results, elapsed validation and delivery are recorded below
after the candidate is frozen. Delivery is source on `origin/dev`; the Windows
installation script builds its archive.

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

## Integrated candidate and focused evidence

The integrated production candidate is `5042b474e`. Only three existing menu
types and the build stamp change: `MenuDepthCharacterSource`, `MenuDepthCardPlane`
and `LevelUpComposite`. No new/removed production type, shader, bundle, native
material, shared compositor/allocator, tracking or actor-authority change occurs.
The compiled comparison against the exact Build736 DLL confirms those three
behavior types and nine unchanged-source ModBuild-only consumers. Known actual
hash/time identities and exact build-number tokens are the only normalizations.

Shadow worker `5c3959e22` integrates as `5042b474e`. Its focused actual GL proof
passes 1276 assertions and five causal controls on 33 stable inputs. Native camera
and light declarations in six scenes confirm the forward GUI3D path. Dynamic
shadow removal changes actual lit pixels; an independently nested scenario camera
retains exactly identical pixels. Both eyes, head motion, pooled renderers,
detached original companions, error/shutdown restoration and original invisible
ShadowsOnly proxies are covered. Original lit materials and light intensities
remain intact. This is Standard-lit witness geometry, not native D3D11 game art.

Card worker `08298adae` integrates as `dd12dd928`. The complete existing card suite
passes 2696 assertions and all 20 controls in 146 seconds, with 41 stable native/
source inputs. Prior cases/tolerances and zero-glyph-loss samples remain intact.
The first invocation retained all positive passes and 19 controls, but its reduced
hover-depth mutation failed earlier at the unchanged geometry oracle. Port only
the expected failure matcher, then rerun the whole containing suite. No tolerance
or product repair followed that fixture correction.

Level-up production/pure `9ba348e3f` and native fixture `0dc61c5b2` integrate as
`e9dbe9015`/`31317f439`. The complete pure suite passes 294 assertions, eight
enrollment checks and 11 controls in 39.2 seconds. The containing actual Unity
suite passes 49 assertions and 14 controls in 39.3 seconds on 18 stable inputs.
It executes original TMP/uGUI/ExtendedButton, actual raster and the owning
raycaster. The exact native TMP reproduces the negative sentinel bounds. Native
candidate/comparison columns remain visible, permission-gated clicks fire exactly
once, locked outside-level browsing still hovers, and close/reopen reuses the
original inventory. Session/actor state is an explicit fixture input; no complete
native actor bootstrap or real card grant is claimed. Three initial fixture-only
failures (combined corrupt inputs, new pixel-classifier calibration and querying
raycast before first render) remain preserved and separately explained.

All three clean worker worktrees are retired after SHA256-verified evidence
archives. Per-run generated caches were removed using the completion API, while
all source, logs, manifests, captures, inputs and failed attempts remain available
in ignored `debug/menu737/workers`. Compact handoffs and retirement receipts are
retained beside the archives. Each worker stopped only its own private ADB server.

## Final integration checks

Strict actual-runtime Debug/Release pass with zero warnings/errors on 1168 stable
production inputs. Fresh metadata Release passes with zero warnings/errors.
All 16 source gates pass in 20.9 seconds. Bundle formats pass; exact before/after
source surfaces remain 670 config keys, 261 Harmony attributes and 4845 log tokens.
The unchanged five bilingual guides/ten files pass docs-i18n. Private compiled
scope and separate original shader-bank provenance remain intact.

The cadence-driven complete local/wire checkpoint passes on this frozen candidate:
all 264 local suites pass in 3198.405 seconds (53.3 minutes), followed by all
306431 assertions in the freshly executed actual wire executable. All 2747 pinned
source/fixture inputs remain unchanged. Every result log hash, inventory membership,
strict/metadata input, worker archive and known failure record is verified in
ignored `debug/menu737/validation-final.json`. This is a fresh complete local pass;
no inherited local/golden pass is needed for this checkpoint. It supersedes the
older aggregate only for current inputs, without erasing failed historical runs.

All owned test/compiler/Unity processes are reaped. Final wire and project caches
have completion-cleanup receipts; 15 untracked Python bytecode caches (1137133
bytes) were also removed. Root stopped only its task-private ADB server. Current
deliverables, full logs, native inputs, source/manifests/captures, protected inputs
and unrelated worktrees remain intact. The three verified worker archives and
retirement receipts preserve both successful and failed focused attempts.

Delivery is source on `origin/dev`; the Windows installation script builds its
archive. Actual D3D11/headset appearance remains a separate acceptance step. The
outside-level browser keeps native lock/reset permissions and does not grant a
free level-up or card. No custom browse/continue button is introduced.

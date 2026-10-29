# Menu laser carry, finite Default and native pause opening — Build744

The maintainer's Build742 review reports a world main menu that can be carried
by hand but not by laser, a Default sky which cannot be left in menu/map, and an
unrelated-looking flash before the controller-X pause menu. Both fresh local
logs identify742/fc74; remote717 is stale. The maintainer explicitly confirms
index-trigger laser carry and the X pause entry point. No new screenshot was
supplied for these defects.

## Repairs and reproduction

The ordinary native physics mask included the dedicated mod layer until a
scenario/map controller replaced it. After actual physics synchronization the
main-menu handle's broad palm trigger was hit at1.475m, before its narrow rod
at1.484598m. The same handle therefore appeared to be a nearer obstacle and
refused laser carry. Exclude render/registry geometry from that initial native
pick and the missing-hand map restoration fallback. Keep authored native masks,
direct rod/uGUI selection, fan/board solid blockers, near offers and established
presses. Actual paused carry, aim movement, both reel directions and release run
through the native hand-driver order, including after hand carry and resizing.

The private Default shell followed the live head and fitted the current far
plane. It now captures one world pose and one scale at acquisition. Its exact
original offset, shape and yaw use the existing rig fallback's12-unit reference
to give a common menu/map radius of about7m horizontally and2.97m vertically.
This reference is a deliberate presentation metric; native scenario auto-scale
depends on its board and remains untouched. Movement, recenter, zoom and clipping
never reposition the occupied shell. Existing clip policy consumes its finite
bounds and performs one identity-bound acquisition refresh before the first
draw. Intro readiness, native arrival, MR and other styles retain authority.

The native pause Canvas remains a root while disabled, retaining its own
ScreenSpaceCamera adapter despite its world host. Assigning the head camera
re-drove its frame onto square head pixels and head pose. Original asset fields
and actual Unity2021.3.5 reproduce the742 log's1920-square rect, scale, tilt and
large local-Z displacement before forced reveal. Under the existing menu-only
conversion flag, record and pin that native root to WorldSpace before parenting.
Restore its mode after its native home and camera; retain refused-home receipts
in the existing retry owner. Ordinary/nested/already-WorldSpace conversions keep
their prior behavior. No general adoption, fitting or reveal algorithm changes.

## Verification and limits

Class B: bounded existing input/menu adapters and private sky presentation, with
the same clipping algorithm and explicit inactive-context controls. Fresh union
Debug and Release compile with0warnings/errors (14.90/14.16s). Source16 passes
in21.4s. The final integrated focused runs pass:

| Scope | Evidence |
|---|---|
| menu-world-runtime | 28variants,166production assertions and27causal controls;82.97s |
| default-sky-runtime | 20variants,138production assertions and19controls;149.30s |
| desktop-render-runtime | 40variants,169production assertions and39controls;137.12s |
| conversion-rollback | 199assertions,11runtime and1binding controls;25.31s |
| menu-rig-lifecycle | Complete existing lifecycle/clipping consumer;32.66s |
| environment-menu-scope | 107production assertions and8controls;33.71s |
| environment-budget-runtime | 11478production assertions,96variants;218.42s |

The two parallel containing runs take218.42s and220.14s respectively; these are
focused runs, not a new complete local gate. Workers additionally pass four
map/near-grab dependent scopes and the two VR Options/closure scopes. Inherit
unchanged areas from final742's input-bound aggregate267 checkpoint. Of262wire
compile inputs,261 are byte-identical; NetProtocol changes only build comments,
whitespace and the intentional742-to744 stamp. Prior306431goldens remain
inherited evidence, not a fresh744 execution.

Private compiled comparison finds exactly6runtime types and9exact build-only
changes, with0added/removed types. The8548088-byte embedded-resource block is
byte-identical to final742 (SHA256
`aa3109acbd11866198a83c69b0dfb8232f61cfd626e06985006e581b527cacdf`).
Config670,265Harmony attribute signatures,4835log tokens and223registered patch
classes/338methods remain unchanged. All12locked frame orders pass.

Root inspected inside/exit/exterior-lookback/map-first-draw sky pixels and the
unpinned/pinned first/settled pause ink witnesses. They execute actual native
geometry/Canvas/CanvasScaler and Camera.Render; the explicit GL sky shader
witness and synthetic UI ink do not certify native D3D11 art, OpenXR timing or
the headset outcome. Build744 hardware acceptance remains outstanding.

All failed development attempts and controls remain evidence, including the
unsynchronized laser false-negative, corrected fixture expectations and native
float tolerance, and the rollback fixture's previously skipped destroyed-host
checks. Per-run cache cleanup follows stopped/reaped processes. All3owned worker
worktrees are archived, byte/hash verified and retired. Evidence is under ignored
`.planning/debug/menu744/`; current deliverables and hardware inputs remain.

Source integrates into `origin/dev`. The Windows installation script builds its
archive. Build743 remains the separate Quest lane; this delivery is no further
Quest rebase authorization.

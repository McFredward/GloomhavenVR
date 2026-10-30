# Native card reveals, confirmation sharpness and card-list access — Build745

At the maintainer's explicit2026-10-29 17:06UTC request, sourcec3bb18117 is
already pushed to `origin/dev` for an available multiplayer hardware tester while
the complete guard continues. Focused evidence and strict builds have passed;
this early delivery does not claim a completed aggregate or headset acceptance.

The maintainer's Build744 review accepts the main character window's sharpness,
but reports an initial rotation flash when new level-up cards are introduced and
strong shimmer on confirmation buttons. A follow-up cannot find the unlocked
card list outside an active level-up. Both fresh local logs identify744/75b3;
the remote717 logs are stale. No new hardware screenshot accompanies this report.

## Repairs

The native `UILevelUpCardHolder.PlaceNewCard` spawns its original pooled card
synchronously for both introduction and hover. Introduction differs by starting
the original half-second alpha tween. The existing pose postfix skipped that
path because `immediately` was false. Align that same original card before its
first visible alpha sample, keeping native pool selection, local position and
scale, fade, highlights, cancellation and continuation callbacks.

Hardware logs explicitly refuse `Confirmation Box_pc` supersampling at the
448MiB session cap. Its producer already has the accepted fixed2x head capture,
but the confirmation's persistent DontDestroyOnLoad host requests an independent
26.4MiB mono allocation. The visible shallow owner membership alone therefore
does not provide the owner's sharpness.

After seating the original confirmation, bind that exact live member to its
producer's admitted capture. Both retain their native scene parents, input and
home receipts. The explicit membership contributes actual member bounds and
private layers to the existing head capture and source permissions. Revalidate
owner/member root and target identity, live conversion, visibility and current
layer authority. Restore only descendants still belonging to that member;
reject cycles and pooled foreign windows. Release owner, member and unadmitted
relationships on their existing lifecycle paths. Allocation limits, capture
quality/rate, resolve, shaders and accepted 3D depth remain unchanged.

The original closed-list toggle is a43x65 arrow in the still-closed, offscreen
inventory sibling. Waiting for that inventory to open leaves its opener outside
the converted character window. Lease the original Toggle into the visible
Cards header, preserving active state, interactability, value, listeners and
native Show/ToggleDisplay behavior. Add an English/German caption using the
visible native header font/material. Pointing at the caption dispatches to the
same original Toggle. Restore exact native geometry/home on exit and retain a
refused return for retry. Never invoke a reset, unlock or gameplay action.

Player path: character window → Cards → upper-right **More Cards** / card icon
→ index trigger; press again to close. German caption: **Weitere Karten**.
The original game's availability rules still decide whether the list is offered.
Both playing guides document this path.

## Focused evidence

| Scope | Evidence |
|---|---|
| Won-card and hover container | 30variants;2808production assertions across5positive cases and25causal controls. Actual native first-alpha rendering, alpha0/.25/.75/1, native half-second completion/cancellation and12hover reuses. Old744 diverges by32678pixels at the first visible sample. |
| Confirmation head capture | 15cases:4positive cases with2416assertions and11causal controls. Actual coordinator/CaptureMenuDepth/ownership dispatch,16moving angled eye views, original TMP glyphs/material and Continue sprite. Button edge error15187646→1036988; identical2x native-glyph correlation≥0.9997628. |
| Card-list portable scope | 345assertions and17causal controls, including original home/identity/permission transitions and retry. |
| Native card-list probe | 42assertions with original header/icon/font, caption hit outside the icon, original ExtendedToggle/Show/ToggleDisplay chain, native disable/recovery and EN/DE pixels;3causal controls. |
| Native confirmation producers | Original generic, Perk and item producer Bind/Apply order with2causal controls; included in the final complete native container. |

The native list probe ports inventory population, campaign services and focus;
the head fixture ports generic admission/allocation at its already-admitted
capture boundary. These are explicit harness boundaries, not gameplay or headset
coverage. Root inspected final original-header EN/DE images and native front and
angled confirmation/glyph captures. All evidence uses Unity2021.3.5f1 and GL;
These GL witnesses do not certify D3D11/OpenXR timing or headset appearance.
The subsequent paired745/c3 hardware review reports separate map stalls,
environment coupling and shared-window issues; those are tracked for Build746.

## Integration verification

Class C: the explicit owned source extends shared capture membership and private
layer ownership, so the actual complete guard is required. Final production is
frozen at017caeae4; subsequent fixture commits produce unionc3bb18117. Strict
Debug/Release build in16.16/20.25s with0warnings/errors. Their production input
hashes remain exact. Final16source suites pass in26.6s. The actual complete267
local invocation records264PASS and3setup/fixture failures in3685.94s. The travel
fixture lacked a read-only decompiled game reference; two older extracted-source
fixtures lacked explicit ports for the new capture handoff/sync calls. Restore
the game reference and add observed, identity/order-checked boundary ports with
causal controls. Production remains byte-identical to publicc3 sources.

Primary containing reruns pass travel36native assertions/10controls in84.6s,
dialog383/29 and capture423/33 in a101.6s paired run. Reuse264 unchanged successful
closures: this is complete aggregate267coverage, not a new successful single full
invocation. The original retained golden apphost actually executes306431assertions
successfully. Bundle/surface checks and the original guard's extracted Release
snapshot/comparison suffix run against the private744 baseline. The full attempt's
raw1 records its fixture failures; the separate suffix's raw1 records the reviewed
intentional compiled differences. Neither exit is silently converted into success.

The complete head container passes95variants (27positive cases/68controls),
including original TMP, confirmation, annotations, motion and4040 raster coverage.
The full original level-up container passes44variants and1116production assertions.
The integrated30-case card container retains its2808positive assertions.

Release decompilation changes6existing runtime types plus9exact744→745 build
constant users, adds2type files and removes0. All8548088embedded-resource bytes
remain identical to744 (SHA256
`aa3109acbd11866198a83c69b0dfb8232f61cfd626e06985006e581b527cacdf`).
Config670,265Harmony attribute signatures and4836log tokens pass, with only the
bounded `CARD LIST ENTRY` marker added. Registered patches remain223classes and
338methods;12locked frame orders and5English/German player-guide pairs pass.
No new Harmony hook or config key is introduced.

The initial isolated gate's sparse fast-forward pruned manually hydrated immutable
assets. One attempt stopped before input pinning; another was interrupted after
three missing-input failures. A subsequent attempt exhausted disk space after
30local successes,8failures and229unrun scopes; no actual goldens executed.
All failed-attempt source/log/capture evidence is content/hash verified in the
1660850280-byte `gate-phases/setup-and-enospc-attempts.tar.zst` archive under
ignored menu745 evidence (95026entries). Source/failed logs and interruption
records remain.
Restore all immutable inputs only after child/compiler shutdown and verify the
asset scope unchanged from744. Restore two original Unity Editor symlink types
without changing their read bytes. The fresh complete run uses4parallel jobs.
These are test setup failures, not game fixes.

All3implementation worktrees, the bounded fixture-repair worktree and the final
validation worktree are clean, archived, fully content/hash verified and retired.
The final gate archive retains315505evidence entries in5388825136bytes, including
its private compiled baseline/current snapshots and actual golden executable.
Completed focused runs clean regenerable caches after process shutdown;
source evidence, captures, logs, manifests and current artifacts are retained.
An older-cache audit additionally removes4304byte-identical movie/story build
copies (8446095280bytes) from4inactive fixture bins and785202837bytes of old
generated caches through the standard completion API. Pins, borrowed inputs,
all original assets and compiled/log evidence remain.
Evidence is under ignored `.planning/debug/menu745/`. Source delivery is
`origin/dev`; the Windows installation script builds its archive. This delivery
does not authorize a further Quest rebase.

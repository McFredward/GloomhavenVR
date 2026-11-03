# Issue24 initiative row review

Worker base: dev583c2282e (qualified Build756), worker/issue24-757. Test classB: bounded local original-row presentation; no native turn/actor authority, protocol or window-placement change. Parent owns strict builds, all source gates, final compiled/surface review, delivery stamp and dev integration. Qualified756 coverage271 local IDs/16 source suites/306739 goldens is inherited only for unchanged closures.

Original issue/media and shared753 logs are preserved in the integration checkout
under `.planning/debug/github-issues-20261103/hardware/`. The initial screenshot
shows a small row above a normal board; it does not establish a singleton swap.
The new comment describes retained interaction with smaller depth offsets. The
uploaded ZIP/log pairs for issues24/27 are SHA-identical and represent one
client, not independent host and observer logs. Both advertise753/b87477f8a.
Only dead teardown rebinds are logged; detailed row/fit matrices are unavailable.

The actual Unity baseline reproduced two source defects against untouched756:
changed-row layout re-latches native world-X leakage as canonical Z because
Unity layout only writes Vector2 anchoredPosition, and a first conversion can
first latch after a foreign world-X Update. The old Build731 claim did not
prevent either path; the relevant slide/dock/pin/fit sources were byte-identical
through published753. Neither baseline establishes the historical screenshot's
trigger, a live singleton swap, a native turn stall or a shared headset outcome.

## Proven path and repair

The original InitiativeTrack.AnimateInitiativeReorder source258–287 creates
world-X easeOutQuad tweens after WaitForEndOfFrame. Original LeanTween IL writes
world X on the next Update; in a rotated VR dock this changes local Y/Z. Native
Choreographer.Update2344–2387 / ProcessMessage12534–12550 can then call
UpdateInitiativeTrack590–647 / NormalizeActorsPool540–583 before surface LateTick.
That pool path neither clears the pending batch nor calls StopUpdateActors.
This supports the direct changed-row schedule as well as the separately tested
case in which a pool change reaches LateTick before a subsequent batch.

The repair is confined to InitiativeReorderSlide plus one OnConverted Prime
call. Prime records the row immediately upon adoption, and a late-observed
rising flag retains that clean latch when a native batch already exists.
Rebuilds preserve each known entry-root plane; newly pooled roots use authored
Z=0, verified from the original player/enemy prefabs in sharedassets6.assets
(SHA256 a3a8e35f77def2253658f999daaa0967055b492fd2063a7ee64a45c6872f18b5).
An adopted in-flight row uses captured origins and original Unity layout
destinations, rather than treating partially moving origins as final slots.
Begin restores the clean pose in its first visible frame, and membership Abort
re-primes the native row before a later batch. Native actor selection, callbacks,
sibling ordering, durations, easeOutQuad waveform, board pose/manual scale,
nested portrait styling and original layout/fitter release remain authoritative.

## Validation record

`initiative-row-runtime` complete run `run-oae9vo59` passed all six production
cases (1029 actual Unity assertions) and all seven compiling causal controls in
40.21 seconds. Cases cover add/remove/reorder, paused Unity time, cold conversion,
the first adopted frame, ordinary late rising flags, Abort/re-prime and lifecycle
release/reopen. The independent layout oracle checks final native order and
slots; real frame samples check native duration/easeOutQuad while the board is
manually moved/rescaled. All positive canonical-plane pixel checks report
Z=0, physical plane error=0 and zero missing red sample pixels.

Controls fail their intended assertions: without changed-row plane restoration,
13.00774 UI units / 5.419895 mm and two missing canonical-center sample pixels;
without conversion priming, 623.8524 UI units / 259.9385 mm and three missing
samples. Unsafe rising, missing Abort priming and missing first-frame repair
also produce measurable Z/plane/pixel failures. Linear interpolation fails the
native easeOutQuad waveform assertion; missing release Abort fails teardown.
These are image sentinel samples rendered through the original Overlay shader,
not a claim that complete original portraits disappeared. Captures were viewed.

The containing `initiative-binding-runtime` consumer passed its 146 actual Unity
assertions, seven controls and identical-world-geometry picker witness in
`run-nv2_65w9`. Its Slide boundary now accepts Prime; this existing fixture
certifies lease/retarget/picker ownership, while the new suite executes the real
slide, coordinator, dock, fit hold and depth normalization. Its pre-existing
runner does not record an overall elapsed timer; no elapsed value is invented.

Untouched-source baseline `run-i2_notw2` reproduced cold failure at 116.7804 UI
units / 48.6585 mm; `run-5u8sff3v` reproduced changed-row failure at 8.043591 UI
units / 3.351561 mm (13.33 seconds). The latter old frontal-view witness did not
lose a sample pixel, which motivated the oblique, nearer-plane final controls.
Failed fixture attempts remain evidence: nullable native port compilation
(`run-7am8zg2a`), excessive native dependency import (`run-jlojl6e1`), an initially
non-moving sort (`run-i2_notw2` changed case), a valid Abort interleaving rather
than the intended direct branch (`run-cbokpimc`,13.09 seconds), and an unchanged
second sort in the first repaired full run (`run-sqcp3m1c`,39.13 seconds).
That repaired run already passed cold/idle/Abort and five controls but is not
reported as a full pass. Final corrected inputs were run as a full13-case suite.
The first three attempts predate timer receipts; their logs are preserved.

Fixture boundaries are explicit: native actor/phase/bootstrap data, acquisition,
popup/mip setup, unchanged portrait pin, and diagnostic Chronos reads are ports.
Editor2021 batchmode does not service WaitForEndOfFrame; the fixture labels an
explicit source-bound EOF phase port before the next real Update, retaining
original LeanTween IL and its frame guard. It uses actual Unity layout/clocks,
uGUI and OpenGL/llvmpipe camera pixels. It does not bootstrap a complete native
scenario, reproduce original artwork/Photon, or certify Windows D3D11/headsets.

## Consumer scope and inherited coverage

Only InitiativeTrackSurface instantiates the slide. Its Tick/LateTick, fit hold,
depth normalization, lease release and OnConverted are compiled unchanged apart
from Prime into the new suite. WorldSurface, tray dock, UiHierarchyInventory and
native Overlay shader are input-pinned and unchanged. The existing binding
suite is rerun because it compiles OnConverted. InitiativePortraitPin executes
after the slide and excludes entry roots; its source and ownership are unchanged.
RemoteInitiativeTrack reads AuthoredDepth and ReorderInProgress and copies the
corrected source rects; neither its copy/selection logic nor those accessors
change. EnemyRevealSurface, InitiativeHoverCardBlock, WorldUIModule registration,
popup helpers, MR backing geometry and wire grammar retain exact source inputs.
Their qualified756 evidence is inherited; parent validates the final source and
compiled union. No full C gate is claimed or repeated by this worker.

The initial fixture checkpoint registered initiative-row-runtime as local ID272
(119CI/16source unchanged). Parent subsequently added the disjoint static-copy
suite in integration, giving273local; final inventory belongs to the parent.

All runs retain source/native/toolchain hashes, generated sources, assembly
inputs, captures, original logs and cleanup receipts. Passing and failing runs
were reaped and cleanup_completed_run removed only regenerable owned caches.
Solid tar.zst archives and per-member SHA verification receipts are durable at
`.planning/debug/github-issues-20261103/issue24-worker/` in the main checkout.
Final row archive SHA256 is
259098dad6ce9cd321dd8605ecdc35b8688d8641baf21d5cce29cdf84864b6d4
(1515 files /125437805 uncompressed bytes); binding archive contains892 files
/75803820 bytes. Public hardware originals remain in the hardware directory.
No GitHub comment, issue closure, local release ZIP or worker push is delivered.

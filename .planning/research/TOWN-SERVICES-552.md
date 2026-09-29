# Town services 552 — hardware report follow-up

## Evidence

The six screenshots in `.planning/debug/npc_probleme/VirtualDesktop.Android-20260924-*`
and current local `LogOutput.log` / `Player.log` report build 551. Remote logs are
older build 500 and do not establish current multiplayer behavior. Visible defects
include the empty laser intercept, expanded Character UI at the enchantress, empty
native handles, black flame quads and temple lettering crossing the book gutter.
The logs do not demonstrate the physical reclaim path; that diagnosis is source-based.

## Changes

- Visible native UI graphics, rather than empty canvas rectangles, determine laser
  occlusion. Passive residents no longer expose torso pointer proxies.
- The original enchantment chooser remains available to its native controller but
  is detached and masked separately from Character UI. Walking away closes the
  native service. Original per-card enhancement-point widgets accompany the offered
  card, separately from character-wide enhancement capacity.
- Offered ability and item cards route physical laser/proximity grabs to the existing
  hand interaction path; the same card enters the hand and pending purchase cancels.
- The priestess offers an original game money pouch in the owned offhand fan position.
  A deliberate release into the original bowl executes the original guarded donation
  and confirmation path. Pending host acknowledgement cannot submit twice.
- Temple text retains original localized content, wraps on separate pages and follows
  the actual native book surface. Inert observer text receives the same projection.
- Mirrored enchantress wrist pronation and varied merchant contact timing replace the
  reported twisted hand and uniform coin-counting beats. Contact positions stay tied
  to the actual coin, ledger and imported rig.
- Quiet spatial native coin, cloth and spell clips follow shared activity contacts,
  respect native volume settings and stop on departure/teardown. No generated voices,
  gameplay sound callbacks or paid APIs are used.
- Native flame atlases contain fully opaque black padding. Coverage now uses emitted
  color as well as alpha, keeping that padding transparent while retaining bright
  cores, original additive halos and shared intermediate animation.

## Verification and limits

Detailed worker evidence: [input](TOWN-SERVICES-552-INPUT.md),
[motion and audio](TOWN-552-MOTION-AUDIO.md). Integration results follow below.
The flame fixture renders all 64 original CandleAnim frames and the static original
CR_CandleFlame_01 texture; a negative shader reproduces build 551's black rectangles.
The original environment bundle is unchanged. A full installation containing the
updated Windows town bundle is required for this build on every VR peer.

Hardware acceptance must cover unobstructed laser travel, native quest/character UI,
repeat approach/departure, physical card reclaim, pouch donation/cancellation, book
legibility, candle edges, arm motion and quiet positional sound. Automated geometry
and input checks do not establish the final headset picture or auditory quality.

## Integrated checks

The complete 69-suite local run covered every existing suite. Two fixture builds
initially failed because their explicit boundaries lacked the new book renderer and
card-slot types; 67 suites passed. Both fixture boundaries were updated, and the full
interaction and mirror suites were rerun successfully. These are retained as separate
runs, not rewritten as an uninterrupted green initial run. A new 70th local suite
covers native card-slot identity, state, per-frame animation and inertness (225
assertions, seven compiled negative controls).

- Original flame render/clock: 1,156 assertions, six clock negatives, billboard and
  coverage controls, including all 65 actual native atlas/static frames.
- Native decor: 84 assertions, 11 negative controls; the original purse and book are
  isolated from native controllers/colliders and retain explicit texture provenance.
- Native book and shared bowl: 243 assertions, 11 negative controls.
- Golden wire vectors: 286,120 assertions. No wire format change.
- Test-runner inventory updated from 69 to 70 local suites; runner self-tests pass.
- Town bundle: 96,196,197 bytes, SHA-256
  `d0564f193b827ed62f2693c264e4bec0e735a7566c08fa8e376ceaaf344af5e4`.
  Both bundles pass the exact Unity 2021.3.5f1 / UnityFS v7 gate.

Final settlement follow-up passes 1,288 production interaction assertions and 50
negative controls; the native transaction/departure suite passes 144 assertions and
12 controls. Accepted purses wait in the actual bowl for native completion, then
sink/fade once; cancelled or rejected offerings return without payment. Walking
away closes the native temple destination after restoring the hand. Both entered
characters and remote relocated workspaces were reviewed.

Fourteen source gates and the strict Release build pass (zero warnings/errors).
The new source-compatible fixture suite is registered in the local scheduler;
hardware-only suites remain local. The final pushed commit must also pass its
associated CI run before delivery. No release is created from this feature branch.

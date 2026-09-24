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

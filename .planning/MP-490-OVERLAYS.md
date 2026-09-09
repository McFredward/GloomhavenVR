# Build 490 pre-hardware overlay review

Base: `97ad6c6a` (dev, ModBuild 489). Worker: isolated `mp490-overlays`.
Both supplied hardware banners still say ModBuild 488 (`LogOutput.log:17` locally and remotely).
Build 489 has no new hardware evidence. These are source-proven repairs, not headset results.

## Corrected output and lifecycle boundaries

- Native appearance samples used to resolve a mutable actor/list/seat/count address on every draw.
  They now bind a model once on receipt and permanently invalidate that sample if its source
  changes. A same-count replacement cannot steal the preceding card's burn/ghost output, and
  interpolation cannot cross model identities. Production samples additionally carry immutable
  class-pool provenance through additive 68 (integrator-owned DTO/codec). The receiver requires
  dynamic and immutable addresses to resolve to the same model before binding. This also rejects
  already-delayed same-count replacements. Supply cards use the high bit of the pool position;
  borrowed cards retain their original donor actor/pool while their visible seat names the recipient.
- Native playback no longer builds the synthetic legacy burn rig just to apply/reset original
  output. That rig assumes the high-detail shader and writes burn constants before actual paint.
  A pending native face now clears all four native restore terms, including `_Burn`, and flame
  `_FXAnim` on private materials, including the original low-detail variant.
- Native authority loss restores the clone's pre-playback graphics, renderer colour, gradient,
  enabled/active flags and CanvasGroups. The previous reset left native-written opacity and
  visibility behind on reused faces.
- `_PosAndBounds` is refreshed after each current footprint measurement, not only when a material
  is first created. Native shader geometry therefore follows ongoing card fitting/movement.
- Graphic visibility samples walk the original hierarchy up to the card root. A disabled parent
  can no longer leave an active child flame/header falsely visible remotely.
- Local `BurnLookPolicy` receives the actual adopted `AbilityCardUI` model and actor from
  `BurnCardFx`. The original `FullAbilityCard.AbilityCard` field is initialized for action cards,
  not all preview/pile/hand widgets, and may name an earlier pooled card. Lost-list membership
  also outranks stale pile stamps in both directions, including recovery.

The native material roles, shader floats/colours, flame texture variant, original gradient flags
and CanvasGroups remain original output. No gameplay controller was added to a clone. Explicit
flight fallback paint remains owned by the flight classes and is not reset by native absence.

Foreign-character focus uses the actual board owner's factory/adopted widgets too. The sampler's
former character-control filter was removed: it suppressed visible foreign-character card effects
although remote boards request output from this board's avatar. Received `RemoteCardArt` clones
are outside that factory and never feed back into the sampler; gameplay authority stays unchanged.

## Validation

- Local fallback flights can freeze the actual original card output without activating a parked
  game widget. Only its detached root visibility belongs to the new flight; inner holder visibility
  and native output remain original. Later synthetic flight writes cannot overwrite a native frame.

- Strict Release: 0 errors, 0 warnings.
- Wire suite with the production binding helper linked: 251,057 assertions (+20, including immutable ordinary/supply-pool and detached-flight seams).
- Negative control: restoring per-draw rebinding produced exactly three regression failures
  (same-count replacement, resurrection, initially unresolved sample); repaired version passed.
- No shared baseline was overwritten. Integrator runs the final complete gate set.

Retest fan/held/recess/active/pile movement; burned-card browser; rest recovery; actor focus
changes; high/low card-effects settings. Automated checks do not establish headset pixels.

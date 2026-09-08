# Multiplayer card parity review — 486

Review baseline: `b4dc8980`, after the 484/485 repairs and with the independent native bonus
animation wire contract present. This is a source review; hardware logs still describe build 482.
The source findings below are implemented. The coverage table states the inspected paths;
visual output and multiplayer timing still require the next headset test.

## Verified fixes

- **C1 — Active-area caption.** `ActivePileViewer` builds a native-font, normal-weight caption at
  local z = -0.0025 with a 0.22 font ceiling and `FreeLabelOrder.Rank`. `RemoteActiveCards`
  reconstructed it bold at z = 0 with a 0.045 ceiling. Both now call `ActivePileViewer.CreateTitle`.
- **C2 — Item usable frames could remain absent.** `RemoteItemFan.TickUsableFrames` committed
  its mask/count cache before `RemoteUsableFrame.ResolveSlots` succeeded. One temporarily missing
  inventory therefore stopped retries until either wire value changed. The raw mask is now
  projected through the current bounded inventory each frame, including same-size actor changes.
- **C3 — Observer-only polling delayed appearance/state.** Held card resolution, pile fan
  resolution and item spent flags waited up to `RemoteBoardContent.RefreshSeconds`, even when
  a live model changed without changing its length. These bounded reads now run every frame;
  cloned artwork retains its existing identity and mip-dirty gates. Privacy gates still run first.
- **C4 — Reordered hand reflow used the wrong index space.** `RemoteHandFan.Rebuild` shifted
  transforms by record 36's model seat, although the previous slabs followed an applied record-44
  arc order. The two generations now map through their respective orders to the same model seats.
  A returned card has no resident pose and retains the existing fist-return animation. Invalid
  mappings are refused; no card identity is added to the wire. Exhaustive four-card permutation
  vectors cover reordered plucks and returns, plus identity fallback and malformed membership.

## Further source-proven fixes

- **C5 — Active matrix resident removal (fixed).** `RemoteActiveCards.Refresh` now reserves
  existing panels by local CardInstanceID before allocating panels to incoming cards. Removing
  a middle card or inserting/reordering cards therefore carries each resident's actual transform
  into its new cell, matching `ActivePileViewer`'s persistent VRCard objects. No identity is sent.
- **C6 — Active arrival grace counts refresh calls.** A one-pass grace implicitly assumed a
  250 ms refresh interval. **Fixed:** initial model-before-event grace is now bounded by the
  existing `NetProtocol.CardFxSeconds`; a known active flight still owns its actual lifetime.
  Refresh frequency no longer changes the wait, and an already seated card never retracts.
  A lost event settles after that bounded lifetime; unreliable delivery cannot promise animation.
- **C7 — Mirrored plume (fixed).** Live `BurnCardFx`
  and `ItemsPile.ItemChip` emitter registries replace guessed remote FX-task edges. Every native
  emitter has its original ordinal, seed, age, playback rate, size/speed multipliers, runtime
  simulation/scaling modes and board-relative transform. Custom simulation frames are explicit.
  The receiver clones the exact original ability/item emitter, removes other emitter components
  from that owned copy, and advances native simulation on the owner's clock. Only a new episode
  clears/reseeds; ordinary samples correct playback time. Authored item/child colour gradients
  remain intact; ability root RGBA comes from the owner's actual `SpawnParticle` result. There is
  no separate remote lifetime, one-shot override or twelve-host cap. Root owns codecs, transport
  limits and avatar scheduling; this lane owns native samplers and rendering. Source-time pose
  interpolation includes nonuniform scale and custom simulation frames, using the tested native
  animation playback clock. An episode change resets that history. After reconstructing the initial
  native age, emission is gated by the owner's actual Emitting flag and the authored module enable,
  so StopEmitting preserves living particles without creating new ones.
- **C8 — Active card return from a hand.** The local active pile calls `SetHome(..., instant:false)`
  after release. The remote matrix currently blanks the held cell and later seats it instantly;
  **Fixed:** its original panel retains the held pose-slot, uses the last held slab's actual world
  position/rotation/width on release, and glides position, rotation and scale at the owner's rate.
  The held slab deliberately retains its last transform after its renderer is hidden.

- **C9 — Consumed/spent item foreground (fixed).** `RemoteCardArt.ApplySpentLook` omitted
  the item's serialized `ItemCardEffects.fgFx` image. The clone now captures that exact reference
  and native frame textures before stripping the component, creates an owned material and applies
  the native foreground constants and final `_FXAnim = 0.5`. Both original item timelines last
  0.001 seconds, so this adds no invented two-second ramp. Item smoke uses C7's original item
  emitter subtree, not the different global ability-smoke prefab.
- **C10 — Floating pile-fan captions (fixed).** Local `PileBrowser` and `ItemsPile`
  create a title above the arc; both remote fan classes omitted the title entirely. All four now
  call the same original caption factory with the same placement, fit, colour and depth policy.
- **C11 — Browser resident reflow (fixed).** Remote browse cards were destroyed and recreated
  at the arc origin on count changes, whereas local `PileBrowser.SetCards` keeps each VRCard and
  relayouts it. Equal-count model reorder also painted a different face onto an unmoved seat.
  Local replicated CardInstanceIDs now map resident position/rotation/scale and hover progress into
  the rebuilt generation; new entries start at the pile. The pure map rejects ambiguous IDs and
  handles equal-count reorder, insertion and removal without putting identities on the wire.
- **C12 — Flight capture semantics (fixed).** `VRCard.FlyToPile` and `FlyFromPile` capture world
  endpoints, arch and rotation once. Remote semantic flights instead re-read rotation and active
  destinations each frame; remote burns re-read both endpoints even after handover. Ordinary flights
  now lock orientation and capture the first resolved active cell after board refresh. Burns follow
  their recess only while stationary and capture their complete flight pose at release. Size stays
  relative to the live board, matching the local card's interpolated local scale under its parent.

## Explicit rulings retained

- Secret selection fronts remain governed by `RevealGate`. `CLAUDE.md` requires that every card
  identity reveal uses this gate. This is an explicit privacy rule, not a performance exception.
- The discard arc is covered during short rest: `RemotePileFronts.Tick` records the user's
  2026-09-07 decision, **“COVER THE PILE FAN DURING A SHORT REST”**, narrowing the earlier public
  pile ruling. Burnt cards remain public in every phase: **“Beim Verbrennen EGAL AUS WELCHEM
  GRUND muss die Karte immer mit der Vorderseite sichtbar sein.”**
- Remote cards are cosmetic and cannot be borrowed: `RemoteHandFan.ReportRefusedReach` and the
  card construction record the 2026-09-07 refusal of copying a teammate's card into the viewer's
  hand. This does not exempt any appearance or motion from parity.
- Mixed local/key-derived language remains an explicit project ruling in `.planning/STATE.md`.
  The review does not replace that with a new language transport.

## Coverage ledger

| Surface | Source paths inspected | Status |
|---|---|---|
| Hand fan | Open/close/swap, map key/cache, original arc order, held-seat removal/return, rebuild reflow, native plume routing | C4 fixed; 484/485 focus/privacy fixes retained |
| Browser fan | Open/close/kind/actor retarget, resident relayout, caption, pile-front gates, size | C3/C10/C11 fixed |
| Item fan | Open/close, spent-state/raw inventory mapping, native foreground/smoke, usable frames, caption | C2/C3/C7/C9/C10 fixed |
| Active matrix | Grid geometry, caption, arrival/held suppression, resident cell glide, pulses | C1/C5/C6/C8 fixed; shared native pulse repair retained |
| Held fronts | Kind silhouette, reveal/source routing, per-list seat resolution, settled effect look | C3 fixed; held pose retained for active return |
| Board cards | Cell glide, card identity, materialisation and settled FX source, old plume lifecycle | C7/C8 fixed; speculative plume trigger removed |
| Semantic flights | Source/destination routing, active identity, curve, orientation, size, lifetime | C12 fixed; shared owner curve and semantic release filtering retained |
| Burns | Owner-release correlation, recess ownership, hold artwork, flight curve/size/capture | C12 fixed; 484 release and front/back ownership fixes retained |
| Plumes | Native spawn/recycle, authored modules, seed/clock, emitter ordinals, transform/scale/custom space | C7 fixed; dedicated bounded stream integrated by root |
| Usable frames | Shared frame geometry/palette, owner beat, raw-slot mapping, source refresh | C2 fixed; shared geometry and owner pulse clock retained |
| Pile fronts | Public/short-rest gates, live membership, clone repaint, settled shader look | C3/C9/C11 fixed; explicit privacy rulings retained |

## Validation

The first attempted build encountered the expected missing `UseBarAnimationSampler` dependency
from the ongoing native-widget integration. Integration commits `e6ed5416` and `a4e5b19b` were
then brought into this worktree. Further validation results will be recorded with each checkpoint.

Checkpoint C1–C4: Release build succeeds with zero warnings/errors. The pure wire suite
passes 221,639 assertions, including 5,192 new fan-reflow assertions. Temporary runner/project
registration was restored; the integrator owns permanent registration. No headset validation
was performed.

Checkpoint C5/C6/C8: Release build succeeds with zero warnings/errors. Resident reservation
is two-pass so an inserted card cannot overwrite a panel needed later in the same list.
Headset validation of active pickup/release and middle-card removal is still required.

Checkpoint C7/C9: Release build succeeds with zero warnings/errors against the agreed plume DTO.
Temporary DTO additions and removal of the old RemoteControlBoard.TickPlume call were used only
for isolated compilation; the integrator owns those files and the stream/codec tests. No particle
rendering, shader output, native pool lifecycle or timing has been claimed verified on hardware.

Checkpoint C10–C12 and plume followups: Release build succeeds with zero warnings/errors. The
wire run reports 235,022 passed and 10 failed: all failures are the independent board/native source
checks whose matching production changes are not in this isolated worktree. The new resident vectors
pass all 2,886 assertions. Root must run the full suite after integrating its board lane; this is not
recorded as a green full-suite run. The renderer reuses `UseBarAnimationPlaybackClock`, whose source
clock, underflow and discontinuity vectors run in that suite. Native particle simulation and rendering
remain hardware checks, including first observation during a live plume, StopEmitting, moving boards,
custom simulation frames and card flight handover. No game data or game-owned network state changed.

Final plume correction: Unity documents `ParticleSystem.time` as playback time within the current
loop, not elapsed time since the effect was enabled
([Unity 2022.1 API](https://docs.unity3d.com/ja/2022.1/ScriptReference/ParticleSystem-time.html)).
The item sampler previously treated a lower sampled time as a new episode, incorrectly clearing
living particles on every loop. A mod-owned activation observer now names actual enable/pool-reuse
boundaries for both ability and item emitters, including disable/re-enable between sampler frames.
Ordinary wrapped ages only correct the native clock and preserve existing particles/random state.
Observers are removed with their sampled hosts or on reset, and stripped from remote prefab copies.
The earlier concern that a looping plume's age necessarily accumulated minutes was unfounded;
no total-elapsed-age warm-up defect is claimed and no arbitrary age clamp was added.

ScalingMode.Local child emitters additionally retain their actual emitter-local scale on the wire.
An owned parent bridge supplies the remaining world transform, so detaching a native child no longer
mistakes inherited parent scale for the local scale that this particle scaling mode explicitly uses.
Hierarchy and Shape modes retain the sampled world transform. Root owns the conditional codec field
and pending-frame history regressions. The final sampler/renderer build succeeds with zero warnings
and zero errors against that DTO; particle pixels and native activation callbacks still require runtime
verification.

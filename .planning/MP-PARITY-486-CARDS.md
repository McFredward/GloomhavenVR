# Multiplayer card parity review — 486

Review baseline: `b4dc8980`, after the 484/485 repairs and with the independent native bonus
animation wire contract present. This is a source review; hardware logs still describe build 482.
The review is in progress. Unreviewed surfaces below are not claimed compliant.

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

## Source-proven gaps still being implemented or coordinated

- **C5 — Active matrix resident removal (fixed).** `RemoteActiveCards.Refresh` now reserves
  existing panels by local CardInstanceID before allocating panels to incoming cards. Removing
  a middle card or inserting/reordering cards therefore carries each resident's actual transform
  into its new cell, matching `ActivePileViewer`'s persistent VRCard objects. No identity is sent.
- **C6 — Active arrival grace counts refresh calls.** A one-pass grace implicitly assumed a
  250 ms refresh interval. **Fixed:** initial model-before-event grace is now bounded by the
  existing `NetProtocol.CardFxSeconds`; a known active flight still owns its actual lifetime.
  Refresh frequency no longer changes the wait, and an already seated card never retracts.
  A lost event settles after that bounded lifetime; unreliable delivery cannot promise animation.
- **C7 — Mirrored plume (fixed; dedicated wire integration required).** Live `BurnCardFx`
  and `ItemsPile.ItemChip` emitter registries replace guessed remote FX-task edges. Every native
  emitter has its original ordinal, seed, age, playback rate, size/speed multipliers, runtime
  simulation/scaling modes and board-relative transform. Custom simulation frames are explicit.
  The receiver clones the exact original ability/item emitter, removes other emitter components
  from that owned copy, and advances native simulation on the owner's clock. Only a new episode
  clears/reseeds; ordinary samples correct playback time. Authored item/child colour gradients
  remain intact; ability root RGBA comes from the owner's actual `SpawnParticle` result. There is
  no separate remote lifetime, one-shot override or twelve-host cap. Root owns codecs, transport
  limits and avatar scheduling; this lane owns native samplers and rendering.
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
- **C10 — Floating pile-fan captions (being implemented).** Local `PileBrowser` and `ItemsPile`
  create a title above the arc; both remote fan classes omitted the title entirely.

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
| Hand fan | Open/close/swap, map key/cache, original arc order, held-seat removal/return, rebuild reflow | C4 fixed; further geometry/effect review ongoing |
| Browser fan | 485 character retarget path; pile-front gates and resolution | Remaining motion/size paths pending |
| Item fan | Spent-state/raw inventory mapping, usable-frame creation/retuning | C2/C3 fixed; remaining motion paths pending |
| Active matrix | Grid geometry, caption, arrival/held suppression, resident cell glide, pulses | C1 fixed; C5/C6/C8 pending |
| Held fronts | Kind silhouette, reveal/source routing, per-list seat resolution, settled effect look | C3 fixed; material/timing continuation pending |
| Board cards | Cell glide, card identity, plume trigger | Full face and materialisation review pending |
| Semantic flights | Source/destination face routing, active identity and flight state | Full curve/lifecycle review pending |
| Burns | 484 owner-release correlation and recess ownership | Full timeline review pending |
| Plumes | Prefab instantiation vs native spawn/recycle and local clamp | C7 requires owner-state coordination |
| Usable frames | Shared frame geometry/palette, owner beat, raw-slot mapping | No additional geometry gap found; timing clock remains under review |
| Pile fronts | Public/short-rest gates, live membership, clone repaint | C3 fixed; particle/material completeness pending |

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

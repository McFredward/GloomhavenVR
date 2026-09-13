# Build 499 long-rest flash: native effects investigation

Base: `317fc045` on `dev`. Read-only native FX/source-history lane, 2026-09-13.
No particle, card-face, burn-overlay, flight or animation behavior changed in this lane.

## Result

The supplied build 498 evidence supports an unwanted desktop-mirror activation at the
remote long-rest burn, not a newly enabled native particle effect. The integrator owns
that UI-lock / FlatScreen fix. Disabling the original burn appearance would remove
intended owner/remote parity without addressing the observed camera-stack activation.

Evidence is from the main checkout's ignored `.planning/debug/LogOutput.log`, whose
line 18 identifies ModBuild 498 / assembly 1.0.0.0. Old remote logs are not treated as
contemporary peer evidence; there is no screenshot of this exact transient.

- Line 113 records the game's card-particle low-spec switch being set to suppress
  CardEffects/MiniCardEffects smoke. The full log has zero `Native card smoke [`
  activation reports and zero `Bounded burn CardSmoke` bind reports.
- Lines 21480–21487 record ModalUI and the creation/showing of a FlatScreen quad sized
  15.74 x 8.85 world units. Lines 21501–21504 name the ScenarioCamera/UI Camera
  render-to-texture capture and opaque-black background clear.
- Line 21506 records frame 52372 with 11.77 ms in FlatScreen. Line 21507 records the
  remote long-rest burn's burnt-pile discovery on the same frame.

A missing particle diagnostic alone cannot prove that no renderer ever existed, but the
explicit suppression switch, native spawn predicate and positive FlatScreen evidence
agree. This is substantially stronger than inferring an incompatible shader from a flash.

## Native execution and ownership

Read-only game reference: `decompiled/GH.Runtime/CardEffects.cs` in the main checkout.
`BurnCardTimeline` (line 508 onward) changes the existing face-image material properties,
text colours and the authored `_uiFxOverlay` image. `SpawnParticle` (line 741 onward)
creates `GlobalSettings.Instance.VisualEffects.CardSmoke` only when the game's
`NoCardsParticles` switch permits it. `CardEffects` neither requests UI locks nor changes
modal navigation context.

The UI-lock owner is instead `CardsHandUI.AnimateCardsLost` (game reference line 1002):

1. Set `AnimatingLostCards`, enter the navigation lock and request a UI lock on this hand
   (lines 1010, 1021–1022).
2. Yield before starting the selected cards' `LostAnimation` (line 1047).
3. Animate/reorder and eventually release the UI lock (line 1108), then clear
   `AnimatingLostCards` (line 1115).

The source therefore has both a pre-artwork and post-artwork lock interval. Watching
only the material burn on an adopted local VRCard cannot cover this native transaction.

`HandSuppression.BeginBurn` has one production call site: `BurnCardFx.Tick`, owned by a
local `VRCard`. The remote art/plume copies do not register it. Existing
`FlatScreen.WantVisible` suppresses its empty ModalUI fallback only while that local
burn registration/tail/latch is present. A peer's native hand transaction can therefore
request the fallback without supplying a local VRCard registration. Native controllers
must remain absent from peer presentation copies; adding them would be the wrong repair.

## Changes inspected and retained

- `ba25f13d` added original-emitter parity and owner sampling in build 486.
- `cdaa274a` preserved actual loop episodes and native Local scaling semantics.
- `ff02d46f` moved construction under an inactive owner and removed game/observer
  MonoBehaviours, Animator and Animation before activation.
- `e104d26e` added original item-tooltip emitters through the same path.
- `589d0bf5` / `e3718313` preserved spent-card burn appearance and released its history
  on recovery/detach. These write card artwork, not modal state or particle shaders.

Current `RemoteCardPlume` applies the owner's board-relative transform, simulation
space, scaling mode, size/speed, seed and playback clock. Its detached Local-scaling
emitter retains its sampled emitter-local scale through a separate parent bridge.
Each ordinal retains one emitter; descendant emitters and native controllers are
stripped while inactive. Creation, pose and initial simulation occur synchronously in
the same tick before rendering. No source evidence here warrants replacing materials,
changing queues, clamping additional particle parameters or suppressing burn animation.

`BurnCardFx` retains its established reversible owner-side world-card reparenting and
root smoke scaling. `NativeBurnEnumerator` observes existing coroutine steps without
altering native yields or swallowing native exceptions. Neither was modified.

## Validation and limit

This lane performed source/history review and reproducible log counts only; it made no
runtime implementation changes requiring an additional build or graphics test. The
integrated transaction/fallback fix needs its own targeted test and required project
gates. A headset replay of a remote long rest is still required to establish that the
reported transient is gone; log correlation is not a captured headset image.

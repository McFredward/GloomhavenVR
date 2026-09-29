# Town interaction and resident motion — build 550

## Hardware evidence

The September 24 local log identifies ModBuild 549 / version 1.1.0. All five new
`debug/npc_probleme/VirtualDesktop.Android-20260924-*.jpg` images were inspected.
The stored remote logs are from September 14 and do not represent this test.

The close merchant views show the belly intersecting the ledger stand and the
resting hand crossing a large scroll. The priestess image shows separated prayer
hands near her face; the enchantress image shows an exaggerated raised-arm pose.
Static screenshots do not measure card lag, leg twitching or animation timing;
those are maintainer observations checked against the responsible source paths.

## Source findings and changes

- Merchant visitation was still wired as a native shop button. Only the cabinet,
  physical inspection and deliberate palm handoff should invoke merchant work.
- Held stock remained under the cabinet while the item solver interpolated a
  transformed previous world pose. The shared ability-card solver works in the
  hand socket instead. The owned-item fan also lacked the normal proximity laser
  stand-down path.
- Both palm displays were flat and fixed to wrist orientation. Merchant release
  immediately returned the submitted card instead of retaining it for the native
  confirmation. The revised display uses the actual card with a finite settle,
  upright suspension and gentle motion, mirrored from the owner's geometry.
- The former priest prayer targets deliberately separated the palms by 70 mm.
  Stance correction also stopped on the final attention-transition frame. Generated
  gesture references and subsequent IK corrections are reviewed together; good
  joint markers alone do not establish natural-looking animation.
- `Chapel.Clutter.Shelf.Individual#2` is a scroll, not a candle. Height-fitting it as
  a 14 cm candle produced the broad roll seen under the merchant's hand. The stand
  now uses the original `Candlelight.Lighting.Torch.Wall#1` candle; related chapel
  decorations use actual candles as well. A small tool moves outside the hand path.
- The old worktop ended at Z=.51 m, while sampled imported coat skin reached
  Z=.368 m at that height. The middle rear edge is shortened to .31 m; side ledges
  end at .405 m and preserve the existing coin contacts. The leather pad and
  supports follow the fitted contour. The approved cabinet stays unchanged.

## Validation boundary

The furniture contact check reads actual imported FBX triangles and Unity-skinned
merchant torso triangles through complete work/attention samples. It also injects
an old-depth tabletop and must detect the resulting penetration. Source, native
callback, input arbitration and remote presentation tests accompany the changes.
Final integrated results and package evidence are recorded at handoff. Automated
checks and desktop renders cannot confirm headset appearance or perceived realism.

## Additional log observation

The first native template preload logs an ambiguous `BattleOverlayCanvas` atlas.
This was a local session with zero peers at that point; no associated capture/apply
failure establishes a visible defect. Preloading scans inactive children before
partitioning, so this warning cannot identify the affected actual widget. The
registry correctly rejects ambiguous descriptors instead of substituting artwork.
Build 550 adds at most four Debug-only failed image paths per lifecycle, guarded
before building the paths, to identify the exact original reference on the next
hardware run. No ambiguity rule or original asset identity was weakened. A remedy
requires that concrete provenance; the warning is not claimed resolved.

## Integrated validation

All 69 local suites ran. Two new fixture defects were corrected: the catalog's
leaked-hover mutation emitted an empty C# statement rejected by strict warnings;
the floating-card mirror fixture failed to restore its rotated source before the
subsequent four-owner pixel case. Both complete affected suites passed after the
repairs, without weakening production assertions or pixel tolerance. The catalog
mutation also recompiles with TreatWarningsAsErrors and zero warnings/errors.
The original all-suite run is retained as a failed run, not relabelled successful.

- Final source gates: 14/14; golden wire vectors: 286,103 assertions.
- Catalog/input: 6,766 assertions, 23 negative controls; complete native mirror:
  230,588 assertions, 17 negative controls. Handoff/interaction/publication detail:
  [TOWN-HANDOFF-550.md](TOWN-HANDOFF-550.md) and
  [TOWN-INPUT-550.md](TOWN-INPUT-550.md).
- Imported motion: 291,765 assertions, 22 negative controls. Complete actual skin:
  1,943 poses with no arm/torso or opposite-arm intersections, six detected controls.
  [TOWN-MOTION-550.md](TOWN-MOTION-550.md) records provenance and limits.
- Exact fitted merchant furniture against animated skin: 794 poses, zero
  penetrations, injected old-depth worktop detected. Decorations: 78 assertions,
  eleven controls. Source assets: 888,814 assertions, nine rendered controls.
- Strict Release: zero warnings/errors; EN/DE documentation check passes.
  Bundle and surface gates pass. Remaining guard stages were resumed independently
  after the fixture repairs; the historical compiled baseline remains unchanged
  (122 changed and 164 added/removed entries, no order-only differences).

Root logs: `/tmp/town550-full-guard.log`, `/tmp/town550-integrated-catalog.log`,
`/tmp/town550-golden.log`, `/tmp/town550-source-final.log`,
`/tmp/town550-guard-resume.log`, `/tmp/town550-strict-final.log`.
Repaired full mirror: `/tmp/town550-mirror-restored-full/run-pdq3fn9w`.

## Package and hardware check

Install the complete 1.1.0 development package for ModBuild 550, including
`ghvr-town.bundle`; a DLL-only replacement omits the fitted merchant furniture.
Windows town bundle: 96,187,501 bytes, SHA-256
`91dedb5717639a9c55780b61ed95cdb65ea733851cdef585bdbbb2c869642492`.
The environment bundle remains byte-identical, SHA-256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
Final local archive identity, per-file hashes and CRC check are recorded in the
private `debug/town550-package-verification.json` after packaging.

Check in the headset:

1. Clicking/hovering the merchant produces no button sound or action.
2. Approach the owned-item fan with the other hand; laser stand-down and rapid
   wrist/grip motion behave like normal cards, including direct hand transfer.
3. Stock and owned items settle upright over the merchant palm, remain there during
   confirmation, and can be reclaimed. Repeat the upright handoff at the enchantress.
4. Aim at multi-page stock and use stick up/down: crank and cards animate, while
   vertical locomotion yields. A nearer window/handle keeps input priority.
5. Review complete work, approach and departure animations: coordinated merchant
   movement, restrained palm-up casting, joined prayer hands and stable legs.
   Inspect belly/worktop and hand/decoration clearance from either side.
6. Observe the same intermediate handoffs and cabinet changes with another player,
   including taking a parked card back and leaving during confirmation.

Offline pose and rendering evidence is not a hardware acceptance of realism,
readability or multiplayer timing. The feature remains on its dedicated branch;
this package is not a release.

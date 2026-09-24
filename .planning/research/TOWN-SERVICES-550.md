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

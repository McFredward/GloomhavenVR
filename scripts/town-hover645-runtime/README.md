# Native enhancement-row hover regression

Run `python3 scripts/check-town-hover645.py` from the repository. It compiles the
production town publisher, codecs, receiver, binding and motion compositor into
the existing Unity 2021.3.5 runtime harness. Results remain in a private
`.planning/debug/town-hover645/run-*` directory. `--source-root` can validate an
integration checkout without changing its files; `--output-dir` selects a
private evidence directory.

The fixture imports the original enhancement-row hierarchy from the supplied
game assets using the existing `town-first-picture632-runtime/export-native.py`
exporter and loader. It retains the original 26 nodes, artwork, text and
materials. The native serialized `ExtendedButton` hover targets `Content` with
a 14-pixel horizontal shift and scale factor 1. The fixture applies those authored
visual changes directly; it does not execute the game's gameplay controllers.
Observer clones contain no autonomous native layout or animator drivers.

## The reproduced failure

A complete native caption/artwork refresh advances `RemoteModule.LastFrame`.
The stationary root's compact numeric sample is then legitimately older and is
filtered out. A subsequent child-only hover still calls `TownServiceBinding.Apply`.
Build 643 unconditionally centered the root anchors at the end of that method,
even for a native row mounted below its exact top-anchored scroll parent. A full
artwork/header update restored the proper anchors and world pose afterward;
the child-only update had no such repair. The resulting anchor change introduced
an unrelated vertical root tween, causing hovered rows to move through neighbors.

The repair keeps parented native root anchors through the existing root-layout
method. That method preserves the separately authored local/world pose while
applying the original pivot and extent. Existing detached callers still pin the
root to its original pivot. No owner hover animation is disabled, and no network
packet format, refresh rate or motion interpolation is changed.

## Evidence covered

- Three adjacent original enhancement rows pass through real capture, encoded
  artwork and independent numeric packets, reception and remote ticks.
- A caption refresh followed by child-only hover must leave the stationary row's
  world Y unchanged at every sampled render-loop frame. Actual owner/observer
  PNG and JPEG readbacks use a fixed authored menu frame, so recentering cannot
  hide an observer-row displacement.
- Thirty-six concurrent native scroll, fit and hover changes check unwanted
  child Y movement; after scroll settles, individual roots must remain alongside
  their neighbors. The intermediate checks constrain Y/Z, rather than claiming
  instantaneous parity while an intended X/scroll interpolation is in progress.
- Four top, center and stretched root layouts with different pivots preserve
  extent and pose with a compact header present or absent. Detached canvas
  root layout retains its pivot-pinning contract.
- Twelve alternating parent-fit/hover/tint cycles inspect all 26 original nodes
  at 252 intermediate samples and compare all settled world corners.

The negative control restores the exact old root-centering and layout-pose
behavior. It must fail the stationary hover world-Y assertion, not a compile,
fixture setup or missing-asset error. `source-hashes.json` records the actual
production and mutated case sources plus the native loader. `manifest.json`,
native export provenance, assertion totals, metrics, Unity log and renders are
retained for inspection.

Early simple fit/hover fixtures passed unchanged Build 643. They did not advance
the full artwork epoch while leaving the root header stationary, and therefore
could not reproduce this defect. A proposed local-position versus
anchored-position rewrite was discarded after those results; the final repair
addresses the independently reproduced root-layout seam instead.

This is an actual Unity renderer and production transport/compositor regression,
not a headset test. It does not emulate WAN latency, invoke the original game
controllers, or certify every intermediate world corner during intentional
motion. The paired hardware video identifies the symptom; a new multiplayer
headset run remains the visual confirmation.

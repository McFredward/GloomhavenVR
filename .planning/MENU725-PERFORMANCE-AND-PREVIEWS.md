# Build725 menu performance and character presentation

## Hardware report

On 2026-10-27 the maintainer accepted the story illustration depth in Build724.
Character previews remained too large and too far behind the window, and became
unrecognizably pixelated when approached. The desired scene sits around the pane,
slightly protrudes and has an appropriate model size. The priority failure was
strong pulsed joystick-forward movement on the maintainer's powerful gaming PC.

Both fresh local logs identify Build724, commit `c5f70993d`, built on
2026-10-27 09:34:53 UTC. The runtime is SteamVR/OpenXR MultiPass, D3D11 on an
RTX4090. Reported eye targets are 4040 by 4040. The remote logs still identify
Build717 and do not describe this test. No new screenshot was supplied.
Byte-pinned inputs are retained under ignored `debug/menu-depth725/hardware-build724/`.

## Measured performance evidence

The log contains 509 reported frame spikes, 449 of which name at least 40ms in
the `MenuDepth` late step. Those samples have median 52.95ms and maximum 85.57ms
inside that step. The earlier first catalogue-load window also records a
one-off 119.49ms; 85.57ms is not the maximum over every loading window. The last performance window reports 4.720ms average and 85.57ms
worst for MenuDepth. Normal frame median is about 11.12ms; representative motion
windows have logic p95 near 65ms. Reported runtime GPU values are not usable GPU
busy-time measurements and are not treated as such.

The current source repeats full window/panel behaviour discovery and two global
include-inactive manager/RawImage searches on a quarter-second timer. It rebuilds
typed bindings with repeated linear searches. That structural work continues in
ordinary gameplay even when no character preview is being examined. The direct
CPU measurements identify a concrete frame-pacing regression; supplied logs do
not independently measure every joystick input gate. Of the 449 hot spike rows
with GC samples, 447 report no collection. The repeating stall therefore generally
is not a garbage-collection pause.

Player.log contains 28 Debug locomotion samples (LogOutput.log contains none).
Fifteen have active requested/applied flight and focus, including frame8468 with
66.3ms scaled and unscaled delta time. They report no zero-scaled requests or
competing world-grab/reel/scroll. These sparse observations support frame pacing
as the measured failure without claiming a complete input trace. The retained
`locomotion-observations.json` and `performance-distribution.json` record the data.

The flight path itself still integrates the selected direction with unscaled
delta time. The repair must remove unnecessary frame work while preserving
ordinary movement, native animation, input authority and shared window geometry.

## Presentation and capture findings

The character portal uses a pane at 0.85 times the native holder distance while
retaining the authored off-centre model scene. Its eye textures simply copy the
native 1024-square render target. Fixed low sample density and deep model placement
can both reduce useful model pixels as the viewer approaches.

Supersampled windows can also draw their automatic mono camera before two explicit
eye captures. A display component can remain enabled while its parent display is
inactive, so checking only the component's enabled flag admits unnecessary hidden
capture work. The repair must give stereo and mono capture explicit ownership,
retain immediate fresh mono recovery and use bounded physical sample demand for
the character's own targets.

## Implementation and validation

Workers own discovery/lifecycle performance, model fit/resolution, and capture
ownership/physical sampling in disjoint worktrees based on current dev. Source
changes, native-profile evidence, actual raster/count/timing proofs, integration
checks and normal archive delivery will be recorded after review. The accepted
story depth data, original colour/dissolve, native callbacks and absolute shared
window geometry remain required. Local GPU/CPU evidence does not certify a new
headset picture or frame-pacing result.

# Town facial runtime fixture

`python3 scripts/check-town-face.py` compiles the production face controller, rig binder,
attention selection, pose math and remote playback into isolated assemblies, then runs them
inside Unity 2021.3.5. Every negative control must compile and fail at its named assertion.

The miniature meshes, heads and camera are explicit fixture inputs; the real Unity transforms,
skinned-renderer blendshape APIs and physics execute. Native service visits, network player
heads, time and logging are explicit boundaries. The fixture extracts the production facial
state declarations; wire validation is independently exercised by literal golden vectors in
`TownFaceVectors.cs`. Only the renderer setter is instrumented to count writes; it still calls
the original Unity API. No game launch, network service, asset mutation or paid generation occurs.

Coverage includes optical -Z actors/imported bone axes, anatomical gaze bounds and convergence,
complete asynchronous blinks, eyelid following suppressed at closure, three facial LODs,
unchanged-weight writes, restoration before repeated native body sampling, source-authority
parity with a moving observer headset, speech curve time, headsets absent, active visitor
preference, sample interpolation, sequence reordering/wrap, process epochs, stale peers,
author handover and missing-stream neutral return. These are runtime tests of the binding and
motion. They do not establish the beauty of the final meshes or headset comfort.

## Imported asset gate

After the asset builder finishes its Linux validation bundle, run:

```sh
python3 scripts/check-town-face.py --bundle-only --bundle /absolute/path/town-review.bundle
```

This focused mode loads all three actual prefabs and exercises the unchanged production rig
and motion code against their imported bones, eye pivots, blendshapes and body clips. It checks
station-facing optical axes, neutral and elevated binocular fixation, every named weight on
all three facial LODs, preservation across native animation sampling, and repeated head reset.
CPU-skinned bounds with actual shape deltas check finite, human-scale geometry. They do not
prove that a neck seam or facial expression looks natural.

Three separately compiled negative controls break the optical frame, head reset and blink
binding. The run records the bundle path, size and SHA-256 in `bundle-evidence.json`, verifies
that its bytes did not change during execution, and writes per-resident measurements into
`actual-prefabs.json`. A rebuilt bundle requires another run. `--bundle` without `--bundle-only`
adds the actual asset checks to the ordinary runtime suite; `--no-negative-controls` is only
a quick positive check.

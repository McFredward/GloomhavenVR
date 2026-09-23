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

The imported asset gate also requires `town-facial-rig-contract.json` inside the bundle:

```json
{"version":1,"residents":[{"npc":"merchant","lods":[{"renderer":"LOD0_0","vertexCount":123,"skull":[0,1,2,3,4,5],"jaw":[6,7,8,9,10,11]}]}]}
```

All three residents and all three facial LODs must be represented. The example indices are
illustrative only. Actual probe indices must be mapped from the original anatomical template's
skull/lower-jaw semantics through the final import, independently of the resulting skin weights.
The authoring contract transports those original source masks through subdivision and FBX import
in a second UV channel, then exports the resulting imported vertex IDs. This also preserves
selection across UV-seam duplication; it does not infer anatomy from the generated rig weights.
Selecting vertices because their *current* imported Head weight is already one is invalid.
The declared vertex count guards against using indices from a different mesh revision.

Each selected skull/jaw vertex must have at least 0.999 Head weight. At all four combinations
of yaw ±50° and pitch ±22°, its CPU-skinned position must remain within 0.5 mm of its rigid
Head transform. The comparison uses the same expression on both sides, once with a closed
mouth and once with JawOpen 0.65 and Smile 0.195; legitimate lip/jaw expression motion is not
mistaken for skinning damage. It runs on every facial LOD. A deliberately mixed 50/50 Head/Neck
weight on a lower-jaw probe must break that same geometric limit in each LOD. This corruption
exists only in an in-memory weight-array copy; neither the mesh asset nor loaded renderer changes.

This catches a neck-weight transition accidentally extending into the chin or beard, even if
whole-actor bounds stay reasonable. It does not certify the movable neck transition itself,
texture alignment, seam appearance, or that the independent semantic probe set is complete;
those still require inspection of the final rendered asset and its authoring provenance.

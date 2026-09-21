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

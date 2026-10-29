# Menu-depth final-head fixture

Run `run.py` through the local `menu-depth-desktop` suite. It requires the existing
developer dependencies: the read-only game references from `worktree-setup.sh`,
Unity 2021.3.5f1, .NET, Xvfb and the shared `~/unitypy-venv` decoder environment.
Use a task-private `ADB_SERVER_SOCKET`. This suite is in the `local` group; the
native exporter adds no hosted or Windows-installer prerequisite.

The native TMP cases reproduce their inputs in a fresh checkout. `native_tmp.py`
reads the original Level9 Continue text's font/material pointers, exports the
complete static font/glyph declarations and decodes its original atlas into that
run's `native-tmp-inputs` directory. It records object identities, original hashes,
the external atlas stream (`resources.assets.resS`, including its consumed span),
decoded outputs, Python version and loaded decoder program files. Source-stability
pins cover the exporter, all Managed metadata read by the type-tree generator,
the source assets/stream and actual decoder files. No previous run or discovery
cache is required. Static atlas mode needs no TrueType import or dynamic glyphs.

The game TMP DLL executes in the private Unity project. The retained canonical
SDF shader source is reconstructed from the existing ActorBarSDF source, with only
its VR foreground extensions removed. Reversing the retained keyword pragmas must
match the recorded original-source hash. Original font floats, colors, keywords
and atlas are applied without replacing gameplay controllers.

Final head pixels come from the actual NativeFlat and converted coordinators.
Independent world-space clones use the same original font data and a separate
fixed-grid area integration. The new isolated glyph oracle requires correlation
above 0.999; correct paths measured above 0.99991, while wrong native projection
softness measured 0.99528 and omitted native-unit normalization measured 0.889.
Existing whole-window and motion thresholds remain unchanged. The new title is
outside the older synthetic B-canvas label's overlap, so that separate ordering
does not contaminate this font oracle. Front, oblique, rear and storage-retirement
samples execute both eyes and both presentation routes. These GL/editor proofs
do not certify Windows D3D11/HMD pixels or frame pacing.

All private render targets, original camera/Canvas state and font objects are
restored/released after each case. The runner retains per-case timings, source
bindings, captures, results and failed controls; its completion path reaps owned
editor/compiler processes before `cleanup_completed_run` removes project caches.

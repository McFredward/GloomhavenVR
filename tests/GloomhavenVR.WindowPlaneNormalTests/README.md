# Physical window plane normals

Run `scripts/window-plane-normal-tests.sh` with the existing private task
`ADB_SERVER_SOCKET`. `UNITY_PATH`, `DOTNET_ROOT` and `GHVR_TEST_SOURCE_ROOT` may
override the default editor, SDK and reviewed production checkout.

This suite compiles the complete current `MenuDepthProjection.cs` against the
installed Unity modules with warnings as errors, then executes it inside the
actual Unity editor without graphics. Native `Transform` matrices, reflected and
sheared bases, `Vector3.Normalize` and the production helper execute directly.
The unused projection methods' XR eye input is an explicit compile boundary.
No window photograph, shader, game controller, XR compositor or network peer is
executed; the menu-depth render suite owns those consumer checks.

An independent double-precision cross product supplies the geometric reference.
The original raw-axis cross, Unity's per-axis cutoff, omitted safe rescaling,
rotation-only orientation, missing invalid sentinel and accepted nonfinite input
are separate compiling controls. The runner also binds the actual head/desktop
consumer source sites to the shared helper. These bindings are source checks,
not substitutes for running those coordinators.

The actual source snapshots, strict compiler output, executed assemblies and
hashes, engine results, numeric witnesses and cleanup receipts remain in
`.planning/debug/window-plane-normal`. The runner reaps its own editor and
compiler processes before canonical `cleanup_completed_run` cache removal.

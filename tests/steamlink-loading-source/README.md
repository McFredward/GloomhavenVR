# Ordinary Steam Link source lifecycle

This focused Unity 2021.3.5f1 Gamma/Vulkan fixture compiles the complete production
`SteamLinkSceneRuntime`, `SceneHeadOutput`, `SceneEyeCoverage`, `SceneEyeOutput`
and `SceneTransparentMaterials`. The shipping coverage-only shader bank supplies
real depth coverage and material derivatives. There is no image/mask atlas.

```bash
python3 -B tests/steamlink-loading-source/run.py \
  --source-root /path/to/production-checkout \
  --output /path/to/private/evidence
```

The default executes positive rendering and the old normally-closed-owner
negative control. `--mode positive` and `--mode old-owner` select one affected
subset. `--bank` selects an explicit bank; Unity defaults to the required
`/home/claw/unity-2021.3.5/Editor/Unity`. No device or Steam process is contacted.

Actual `Camera.Render` executes the production pre-cull/image callbacks, depth
commands and material pre/post-render scope. Assertions cover opaque A1,
fractional overlay alpha, unchanged contributed RGB, and positive background RGB
at A0. Ordinary output retains full source dimensions, uses Unity's normal MSAA
resolve, refuses reduced-size destinations, and rejects unknown/stale eyes and
unsupported formats. Material scopes restore original renderer materials.

Loading cases disable/re-enable the real GameObject, remove/replace/destroy the
camera, toggle MR ownership, and recover from temporary Mono metadata. A real HDR
attachment failure remains refused until reset. The old-owner control passes its
initial raster and fails the exact re-enable recovery assertion; it is not a
passing implementation.

The fixture supplies explicit connection/capability, configuration, allowed-head,
MultiPass, eye-label and frame-group boundaries. The complete production callback
bodies run unchanged except the named XR metadata reads and `ExecuteInEditMode`
attribute needed by the Editor. There is no physical XR session. Cohort state
assertions alone do not prove atomic native stereo-alpha admission: depth commands
and transparent material scopes are already armed before the final image callback.
The companion's eye/submission association and visible headset picture require
separate checks. Embedded-resource extraction and the actual MixedReality owner
are also outside this fixture.

Sources, transformations, hashes, actual GPU results and expected failures remain
as evidence. Stopped runs call `cleanup_completed_run` and remove only their owned
Unity/build directories after child processes have exited.

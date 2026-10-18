# Actual scene-source loading lifecycle

This is a focused Unity2021.3.5f1 Gamma/Vulkan fixture, not a headset or complete
game test. It compiles the complete production `SteamLinkSceneRuntime.cs`,
including `SceneHeadOutput`, together with the actual `SceneEyeCoverage`,
`SceneEyeOutput`, `SceneTransparentMaterials` and frozen atlas constants. Packing,
depth alpha commands and the transparent overlay derivative come from the
actual shipping `scene.bundle`; the known original Overlay shader is also real.

```bash
python3 -B tests/steamlink-loading-source/run.py \
  --source-root /path/to/production-checkout \
  --output /path/to/private/evidence
```

The default runs one positive fixture and one causal old-owner negative control.
`--mode positive` or `--mode old-owner` selects one affected subset. `--bank`
can freeze an explicit shipping bank; `--unity` defaults to the required
`/home/claw/unity-2021.3.5/Editor/Unity`. No setup, device connection, camera API,
network request or SteamVR restart is performed.

## What executes

Real `GameObject.SetActive(false/true)` invokes the component's real `OnDisable`.
The same camera then calls real `TryEnter`, which must prepare a new component
when the old output was normally closed with an empty Failure. Actual
`Camera.Render` invokes production `OnPreCull`/`OnRenderImage`, the coverage
command buffer and real material pre/post-render callbacks.

Additional bounded cases cover a zero-camera gap, master-MR ownership loss and
return, camera replacement/destruction, a real HDR attachment fault and temporary
Mono metadata. A real attachment fault remains refused until explicit reset;
temporary Mono closes normally and permits a fresh stereo epoch. Each
accepted resume checks all twelve atlas guard cells, opaque depth coverage,
fractional overlay coverage, contributed RGB and positive RGB at zero opacity.
The real material scope must restore the original renderer material after each
camera draw.

The initial observed positive run passed436 assertions,11 camera renders and
five resumes with96 checked guard cells. Restoring only the old owner branch
still passes the initial raster, then fails the exact semantic assertion
`Normal camera re-enable must prepare a fresh scene output` after58 checks.
This is an expected negative result, not a successful old implementation.

Build701 additionally warms each fresh output with one complete explicitly
labelled Left+Right frame. Both actual raster images remain ordinary until the
following frame boundary, so creating the effect mid-pair cannot pack only its
remaining eye. The updated focused run passes499 checks,27 camera renders,
six resumes and96 guard cells. The unchanged old-owner semantic negative still
passes its first raster before failing the exact recovery assertion after66
checks. The separate `steamlink-scene-startup` suite supplies the wider admission
and metadata-cohort semantic negative controls.

## Explicit boundaries

- The test adds only `ExecuteInEditMode` to the real `SceneHeadOutput` type so
  its unchanged callback bodies execute in the editor. It does not substitute
  any lifecycle or rendering method. This is not a PlayMode/XR lifecycle proof.
- MultiPass, current-eye, stereo-enabled and XR frame-group metadata are
  controlled test inputs. The one `Head.stereoActiveEye` read uses the named
  metadata adapter; no alternating-eye heuristic or physical headset is involved.
  Both left and right labels run. Actual Editor `Camera.Render` has no XR device
  or XR frame cadence; only that metadata is supplied, with real callbacks and GPU.
- Session, connection, configuration, allowed-head policy and native-camera
  admission are explicit host boundaries. The actual MixedReality owner,
  identity helper, standalone backend and real settings transitions are not
  compiled or certified by this fixture.
- The asset-loader boundary reads the actual bank from a pinned local file;
  embedded-resource extraction and native transport are outside this scope.
- Readback, probes and fixture allocations occur only in test code. Production
  render callbacks remain the actual source under test.

Sources, transforms, exact assembly/bank hashes, successful and expected-failure
results are retained. Every stopped run calls `cleanup_completed_run` after
reaping children, then removes only its exact owned Unity/build directories.
The first two setup failures (missing Unity module reference and a missing
nested enum/JSON reference in the test boundary) are retained separately and
are not counted as production regressions or passing negative controls.

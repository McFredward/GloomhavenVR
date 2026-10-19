# Owned menu-head lifecycle

Run `python3 tests/menu-rig-lifecycle/run.py`. Use `--output PATH` for retained
evidence, or `--mode production` for a bounded positive rerun. The default runs
production and three compiling causal controls serially in separate owned Unity
projects. It requires the installed Unity2021.3.5 Editor and a Gamma Vulkan GPU;
it is a local suite, with no native receiver, headset or game installation needed.

The harness extracts the actual shipping `UpdateRigLifetime`, menu construction,
live-anchor resolution/rebind, owned camera/root creation, clip/clear maintenance,
menu recenter, teardown and `OnDestroy` bodies. Real Unity GameObjects, disabled
components, inactive hierarchies and destroyed-object null semantics exercise
twelve rapid Intro/MainMenu replacements. The same camera, tracking component,
AllowedHead, rig pose, scale and pose version must survive every native-anchor
gap. Native suspension ownership remains distinct from native deactivation.
Rebinding saves the successor's authored vantage for deliberate recenter while
preserving current head render settings and updating native depth/far metadata.

All four shipping scene-source, coverage, ordinary-output and material classes
execute against the actual scene shader bank and real `Camera.Render` callbacks.
Explicit current-eye/frame/XR attachment metadata replaces the absent XR device,
using the existing loading-source harness's documented metadata transform.
A return-value observer counts successful calls after the unchanged actual
`SceneEyeOutput.Render` returns. Every fresh source keeps both warmup eyes ordinary;
every admitted later eye supplies fresh depth/coverage and changing current RGB.
The observer retains no image, mask or texture. Source alpha alone cannot certify
admission: the coverage command buffer already runs during the warmup callbacks.

Explicit MR off/re-enable, VR stop, real owned-camera/root loss, explicit rebuild,
scenario/map routing and production driver shutdown preserve their teardown and
fresh-source rules. The original anchor-teardown mutant must reproduce the source
close during the first gap; the other controls incorrectly move/recenter the rig
or overwrite the current head's clear policy during rebind.

External game services, the pose provider, camera-policy sweep and MR owner are
explicit boundaries. Scenario/map builders verify routing rather than independent
seat algorithms. The complete production MR owner, receiver and physical HMD
tracking are not executed, and these tests do not certify the headset picture or
the duration of a hardware blackout. Strict whole-mod builds and the existing
render-quality/startup suites supply complementary evidence. After child reap,
`cleanup_completed_run` removes generated caches and preserves sources, project
manifests, logs, assemblies, GPU results and mutation receipts, including failures.

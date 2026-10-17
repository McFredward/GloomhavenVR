# Stereo video menu and separate-media delivery — Build685

## Hardware evidence and explicit request

Both supplied 2026-10-17 LogOutput.log/Player.log banners identify Build683.
The command line includes the exact calibration token. Both logs report the
missing `BepInEx/plugins/GloomhavenVR/Movies/Calibration/calibration-sbs.mp4`
before a decoder or picture is created. This establishes a missing deployed
media input; it does not establish an eye-routing or headset rendering defect.
Source-hashed input copies are retained in the main checkout at
`.planning/debug/stereo-video-menu685/hardware683/`.

The maintainer requests a button under VR Options > Advanced instead of the
launch parameter, then explicitly clarifies: movies must ship **with the DLL
in the installer ZIP, not inside the DLL**. The old private diagnostic update
contained the films but ordinary build/install/release packaging did not.

## Resulting behavior

A direct action on the Advanced index starts the local test. It shows live
English/German status for preparation, stereo/mono playback, closure, missing
media and failure. The action never rebuilds the options page or changes native
focus/availability. A request outside the offline VR main menu is refused with
visible status, never queued for later. A second press while preparing/playing
retains the existing decoder/picture; after closure or failure a deliberate
press may try again. Old callbacks cannot affect a new decoder. Legacy argv is
inert, including an exact old token, and can be removed.

The existing independently owned silent decoder, nine-second chart, fixed
picture, eye selection, dimensions, UI material, native movies/audio/skip/flow
and wire behavior retain their reviewed contracts. Only deliberate menu input
activates media work; reading status and ordinary startup do none.

Both byte-identical reviewed MP4s are tracked separate content. Debug/Release
output, the Windows install script and both release archive paths include them
beside the plugin under Movies/Calibration. The existing intro is preserved.
Preflight rejects missing source media before deployment/package replacement.
Only the tiny existing UI shader remains embedded; no movie resource is added.

## Validation and limits

The complete production owner/policy/localization fixture passes198 assertions
across14 fresh processes and29 causal controls, including12 source-bound
Tick/Shutdown controls. Exact legacy argv remains inert; explicit restart after
close/missing-media repair/error and stale previous-attempt callbacks are covered.
The first control run failed compilation of a deliberately constant-false guard,
then a named online-gate expectation was corrected to its earliest causal
failure. These attempts remain retained; neither is counted as a control pass.

A strict isolated Release build initially exposed three nullable errors in the
parallel dev material helper. Its owning integration supplied the correction;
merging that commit restores zero-warning/error compilation without changing
this calibration's scope. Initial198/29 receipts remain valid for unchanged
runtime inputs; final integrated evidence is recorded with the delivery receipt.

This is a bounded follow-up to Build683. It inherits unchanged shader/media,
GL/Vulkan eye/mask and native-film evidence from that stage. It does not claim
a new complete local gate or a correct headset picture. Actual fusion, per-eye
decoder timing, D3D11 headset output and converted films remain unverified.
No inference or conversion is performed by this repair.

### Final merged source checks

After merging684 and the isolated shader-table repair, all16 source suites pass;
the six affected local suites pass (native-video, vr-options, vr-options-close,
player-settings-help, stereo-video-calibration, self-update-archive). The owner
fixture again passes198 assertions/14 processes/29 controls. The options suite
includes its existing7339 assertions/21 controls and the added25/8 button checks.
Inventory discovery reports19 tests, with17 executed and2 conditional skips.
Strict Release compilation has zero warnings/errors.

The first fresh golden execution found300,517 passes and one strict shader-name
registry failure introduced by684's original material identities. Its owning
integration registered the nine actual source-validated environment shader paths;
no guard exception or whitelist was added. After that bounded repair the fresh
full golden executable passes300,536 assertions. This is the golden executable,
not a new complete local-suite run. The unchanged Build683 whole-gate, shader and
media proof is inherited explicitly rather than rerun or relabelled.

Regular pipeline tests pass34 Python +19 actual PowerShell assertions, including
both normal Release/default and explicit Debug archive paths, ten missing-file
controls and invalid configuration refusal. Actual Debug/Release MSBuild content
copy targets pass15 checks with the original intro unchanged. Resource inspection
on the actual Release DLL finds the reviewed stereo shader and no embedded movie
resources. The final main-checkout Debug build, full regular archive, exact ZIP
media/DLL readback and downloaded-byte hashes are retained beside the handover.

Root evidence is relocated without losing logs, results, generated source or
failure receipts to the main checkout's
`.planning/debug/stereo-video-menu685/integration/`. Worker evidence is under
`ui-worker/` and `package-worker/`; raw hardware inputs remain in `hardware683/`.
Only stopped, owned caches/worktrees are removed. Current Debug deliverables,
source/media pins and shared game references are preserved.

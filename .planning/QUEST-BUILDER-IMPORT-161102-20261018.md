# Quest Builder: retained figure helper in Wizard updates

## Evidence and cause

Report `quest-build-support-20261018T161102Z-e4b127c7.zip` identifies delivered
B690, source `7fd7450469a0ccfba807df3fa9e6b40332a7e4e4`. Current update session
`3bb2de1e2dfb4f9597d0739f1700fbf8` stops inside `Engine.stage_unity`, while
comparing the existing APK's figure bank with the selected source. Its actual
trace is `update_requirements -> _figures -> figure_banks: from storage ...`,
ending in `ModuleNotFoundError: No module named 'storage'`.

The installer loader intentionally restores its temporary module aliases after
capturing the selected Builder. Figure planning incorrectly reopened its helper
later, outside that dependency context. Tests that put the Builder directory on
the global search path concealed the deployment defect. This attempt failed
before launching conversion, Unity compilation or APK modification; older build
logs bundled in the support archive are not evidence of work by this attempt.

## Repair

Load optional `figure_banks` inside the existing isolated installer dependency
boundary and retain it on that exact Builder. Both requirements planning and
actual figure production select that captured object. Preserve the direct CLI
fallback, source-qualified storage functions and exception identity, restoration
of every preexisting module, and the original search path. Older captured source
trees without the optional helper remain loadable.

This is a host-only B690 repair. Runtime, figure producer recipe, original bank,
preparation identity, Unity project, native helpers and payload ABI are unchanged.
No runtime version bump or local APK/Unity/shader rebuild is required. Keep the
user's `.ghvrq` results and resume the failed update with the corrected Builder.
The first B690 update from an older APK still needs its already-described small
code/figure update; this repair does not claim those additions are free.

## Focused qualification

65 checks pass: five fresh isolated Wizard loader cases, five installer alias/
failure cases, seven Wizard compiler-requirement cases, 45 update-driver cases
and three figure-bank cases. The new fixture executes the real discovery loader,
`Engine.stage_unity`, update requirements and figure planner with no Builder
directory in `sys.path`. Only owned-input metadata, APK metadata and external
provisioning are fixture boundaries. It covers cold and retained-bank decisions,
a hostile host `storage`, preserved stdlib `profile`, and a selected snapshot in
a path containing spaces. Removing the repair alone reproduces the reported
`storage` exception at the actual Wizard boundary. Planning launches neither
conversion nor a compiler and leaves the metadata-only APK untouched.

Recheck this fixture against the final extracted archive and qualify its real
launcher/discovery/support APIs before publishing. Retain test logs and cleanup
receipts under `quest3-local/build/evidence/B690-update-161102-20261018/`.
Inherited B690 runtime and browser evidence remains valid only for byte-identical
sources. These focused checks are not a new full gate, Windows build completion
or headset outcome. An independent read-only import audit found no second
concrete local-helper import blocker on this update route.

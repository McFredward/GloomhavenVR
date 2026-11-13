# Quest Wizard startup and homepage

## Requested behavior

On arrival, highlight Mod Update when a newer bundled mod can use an existing
completed local APK and retained input files. Print a wait notice before Python
provisioning so the console explains the delay before the browser opens. Remove
the repeated upper logo, make Home easy to reach and show continuing work
prominently there. Make the existing GitHub and Buy Me a Coffee actions more
visible. Navigation must not cancel, resume or replace an active operation.

## Discovery and operation ownership

An additive `updateRecommendation` in discovery compares the bundled ModBuild
with the completed workspace APK's closed identity. The default APK contract is
unchanged. Check only bounded closed JSON receipts, exact file sizes and the
PC installation's ordinary required-file presence. Follow at most eight exact
base APK owners to find retained Campaign content or an unchanged imported
portable package; do not scan arbitrary build trees or read large payloads.
Embedded mod content does not require a second external mod-bank copy.

This is a presentation hint, not an APK signature or PC ownership certificate.
Actual update admission still validates the selected files and purchased PC
copy. Missing content/game/source inputs prevent the ready highlight. A current
or newer installed runtime does not get an update recommendation merely because
the Wizard source commit changed. No network check, provisioning or build is
triggered by opening the homepage.

The prominent Home action stays reachable while scrolling. A separate homepage
status card summarizes measured build/update, device, export or storage activity
and returns to the same operation's details. Its existing polling continues on
Home; the click does not call cancellation or create another job. Workflow pages
retain their own controls and the globally available support-log export.

Keep one main GloomhavenVR/Meta Quest hero. Use labelled GitHub/Support actions
beside the existing artwork, without adding new remote art or promotional copy.
English/German browser strings remain in the existing Wizard localization module.
Windows/Linux launchers print the startup/browser wait notice before the local
Python bootstrap; `--no-browser` accurately describes preparation only.

## Validation and delivery boundary

The new discovery tests cover actual historical closed build shapes, completed
update chains, missing/changed inputs, receipt races/cycles, a retained package,
same-B676 host-only commit changes and an 11GiB sparse content bank while
forbidding payload reads, hashing and directory scans. The affected discovery,
session, workspace-default and source-metadata suites total50 passing cases.

Actual Chrome witnesses cover initial asynchronous recommendation, ready-only
highlighting, one hero logo, active-job polling on Home, return to live details,
device capture continuation and accessible navigation at desktop/mobile widths.
Inspect screenshots as well as the browser assertions. Final exact test counts,
packaged launcher/API checks and source classification are recorded in
`/home/claw/quest3-local/build/evidence/wizard-home-20261017/` and the handoff receipt.
Linux launch behavior is exercised; native Windows launch is source-reviewed,
not claimed as an end-to-end hardware run in this environment.

These changes ship a new game-free Builder archive with runtime B676 unchanged.
They must not alone cause original asset conversion, art-bank rebuilding or
IL2CPP compilation. Compare the exact previous/current released code/art/native
input rows before publishing. Preserve the prior B676 headset-test result boundary;
no new headset rendering claim or complete unrelated gate is made here.

# Quest installation recovery choices — capture191308

## Hardware evidence

`quest-build-support-20261014T191308Z-def2c86b.zip` identifies Builder source
`17cb1072bce66fa52b64254d5bd79dbfb9da7ed2`, run
`b0a1cdab3326426a9d8d026cd79d5cf4`, and the unchanged B672 APK/input
`d4e76da00ec2`, SHA256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The current named ADB log is now included in the support archive. No replacement
Player export, conversion, Unity import or shader compile is needed for this repair.

The canonical MAIN/LAUNCHER/VR start still produces no package PID. The first
start's ActivityTaskManager line reports `BAL_ALLOW_PERMISSION`, result0; this
does not establish a background-start denial. The second observation reports
Awake, installed=true, stopped=true, and no waiting app process. The JDWP helper
has not been reached. Do not claim that another debugger-protocol change fixes
this headset's launch policy, or that Android exit0 proves a usable activity.

The retained signing transaction is still restoring the original data.
Private/device-protected archives finish; the external root cannot be opened
using ordinary run-as. Original host archives remain authoritative.

## User decisions

The maintainer explicitly requested useful alternatives when automatic repair
cannot continue, then explicitly requested a radical clean-install choice.
The current failed restoration offers two separate choices:

- Manually launch the app from Quest Library / Unknown Sources, after the PC
  Builder arms a debugger wait. Wait for a fresh owned process and the existing
  strict pre-game suspension proof, then restore the original data.
- Delete all Quest app data and reinstall the already completed APK. This needs
  explicit destructive confirmation: current and archived saves/settings on
  the Quest are removed; original PC backups remain but are not automatically
  replayed. Missing game banks require their normal transfer again.

Retry and finish-without-installing remain available. Neither a retry, timeout,
connection loss, diagnostic export nor stale UI action grants reset consent.
The session action is bound to its attempt nonce, completed build key, full
APK/input identity, exact headset and recovery transaction. Changing build
choices invalidates the approval. An install-only decision cannot invalidate
completed asset/Unity owners or initiate a new Player build.

The destructive transaction commits its own reset intent before removal,
resumes replacement installation after interruption without repeating removal,
and never overlays old saves onto new saves in a later ordinary retry/update.
A terminal reset transaction is distinct from successful restoration; receipts
report `saveDataReset`, not pretend that saves were restored.

## Validation

Focused protocol, installer, Engine/API consent and actual browser controls are
required. Check the assembled, extracted game-free Builder and its previous
producer identities. Hardware launch/restoration/reset behavior is unverified
until the maintainer runs the new Builder. No fresh complete mod/runtime gate or
locally generated APK is implied by these host-only changes.

Evidence and run cleanup receipts live outside the worktrees under
`/home/claw/quest3-local/build/evidence/B672-install-options-191308-20261014/`.

Final focused evidence:217 existing/manual installer cases,25 final manual/reset
controls (ten overlap the first group),132 Wizard/API/progress cases and83
producer/release-compatibility cases pass. The42 root browser/model cases include
real Chrome confirmation, cancellation, stale-action and request-order controls.
Worker receipts retain their separate44-case UI and228-case installer executions;
these are overlapping evidence, not additional unique coverage. Each owned run
was reaped and passed to the central retention API. The read-only safety review
has no outstanding findings at its eleven recorded source hashes.

The whole previous delivered source inventory retains completed preparation,
recovery and compact-update producer scopes. Actual extracted delivery smoke
checks the Bash launcher/venv/browser, current installation-log export and the
real reset-consent API, including rejection of a consumed nonce without creating
an install/build job. Existing JDWP protocol and B672 runtime evidence is inherited
for unchanged bytes. No firmware-specific automatic-start cause has been proved;
manual launch and explicit clean installation are concrete alternatives, with
headset execution still requiring the next hardware test.

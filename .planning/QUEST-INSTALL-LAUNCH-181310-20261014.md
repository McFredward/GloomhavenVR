# Quest launcher and workspace installation evidence (2026-10-14,18:13 report)

Keep ModBuild672 /1.1.1 and the already completed Windows APK. This round repairs
host launch/admission and diagnostics, without another game conversion, Unity
import, APK export or change to a player's original save backups.

## Current hardware evidence

`quest-build-support-20261014T181310Z-b9742904.zip` identifies Builder6092efad7
and APK input `d4e76da00ec22d7a615f5cd16f4e930adda0a0c704c70996536cd39606b2ef9a`,
SHA256 `191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
Run `b0a1cdab3326426a9d8d026cd79d5cf4` has six completed earlier stages. Its ninth
installation stops after17.026seconds at access40/41. No JDWP fallback runs.

This is a workspace export from the home page. It has no selected session and
contains progress/build/inspect logs, but lacks the current install log, exact
ADB child log and signing recovery frontier. That omission is a Builder defect.
The maintainer subsequently provided both missing log tails as text.

Authorized USB still works. The original private and device-protected10,240-byte
archives restore before the failing launch. Android initially reports Asleep;
the installer requests WAKEUP once. Its second observation reports Awake, but
the installed package remains stopped and all twenty second-attempt PID probes
return empty/exit1. The start command returns only StartingIntent with exit0.
Its same-package transient debugger choice still has original=null, wait=false.
Thus no new owned process is proved even after wake; this is not an observed
JDWP or file-transfer failure. Empty filtered activity logs do not identify the
particular firmware start policy, and do not prove no short-lived zygote ever ran.

## Bounded launch repair

The old exact-component request omits the documented launcher structure. Use
one shared host function for the same qualified component with ACTION_MAIN,
CATEGORY_LAUNCHER, Meta's VR category and NEW_TASK/RESET_TASK_IF_NEEDED flags.
Use it both for storage-debug startup and the normal post-install/access-repair
launch. Keep the transient debugger choice and all fresh PID/UID/starttime/wait
and native-pause proofs. Do not use `-W` for a pre-Application debugger wait;
window-draw completion would be blocked by that very wait.

AOSP11–13 explicitly maps internal START_ABORTED to external START_SUCCESS.
StartingIntent/exit0, even without a warning, is consequently not launch proof.
Record the bounded exact reply and attempt as a request observation while
continuing to require the actual process boundary. A known awake/no-PID outcome
has its own `restore-app-launch-unavailable` action. It retains the APK/backups,
retries only installation and requests current startup diagnostics rather than
repeating unsupported sleep/USB advice. Unknown/asleep observations retain
their existing wake/connection guidance.

The launch-contract mismatch is source-proven. Whether it explains this Meta
firmware's admission failure is a hardware hypothesis until the next test. No
presence-sensor bypass, global app-switch override, debugger reset, arbitrary
launcher input, extra uninstall or full content backup is added.

## Workspace diagnostic repair

Global reports retain workspace scope/session=null and explicit associations
for up to three recent installation runs. Include each directly owned install
log, its exact current tail-named ADB log and the appropriate recovery summary.
Bind that summary to the named run's completed build identity; never borrow an
unrelated workspace latest-build pointer. If required metadata is unavailable,
record that limitation without failing other diagnostic exports.

Prioritize current installation evidence under the existing file/byte limits,
retain redaction and link/mtime/path guards, and avoid scanning save/game/cache
trees. Per-session exports retain their existing named context and bounded
legacy receipt fallback. No arbitrary device-log sweep is introduced.

## Evidence and remaining limits

ActivityThread emits the wait log before its original synchronous waiting-display
call reaches Debug.waitForDebugger. A narrow debugger-entry seam handles that
interval only with the exact boot Proxy/Binder ancestry and a readable live
`waiting=true` argument. Use bounded owned MIN/INTO Suspend_ALL events to reach
the actual Debug wait before any Runtime invocation. The same post-wait Proxy
called with false, Application, foreign process and unknown metadata paths stay
rejected. This addresses a separate source race that today's no-PID path never
reaches; it does not relax the original invocation boundary.

Focused tests exercise the actual host launch/proof code with a policy-admission
negative control: a bare start returns success without a PID, while the canonical
request reaches the owned debugger boundary. This control models the documented
contract; it is not a Meta firmware emulator. Existing signing/save/installer,
Wizard and browser checks remain focused. The exact AOSP result-mapping methods
also execute in a small Java control with15 assertions. No game runs in it.

The handoff receipt records final source hashes, affected case counts, complete
previous-release producer compatibility, extracted release startup and cleanup.
This host repair is not a new complete runtime/wire gate or a successful Windows
game build/headset installation. Resume the retained Windows run and confirm the
hardware result. Preserve the original nonempty recovery archive and workspace.

The independent audit additionally records an ambiguous historical same-package
debug choice and the older native-process cleanup's weaker termination proof.
They are distinct source findings, not explanations for this orig=null/no-PID
capture. Debugger-wait stack qualification remains mandatory; a wait log alone
must not authorize an invocation during Application or game code.

Primary references: [Android main launcher intent](https://developer.android.com/reference/android/content/Intent#makeMainActivity(android.content.ComponentName)),
[Meta native manifest](https://developers.meta.com/vr/documentation/native/android/mobile-native-manifest/),
and [AOSP Android12 ActivityStarter](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/android12-release/services/core/java/com/android/server/wm/ActivityStarter.java).
Compact supplied capture, primary-source snapshots and final test/cleanup receipts:
`/home/claw/quest3-local/build/evidence/B672-install-jdwp-20261014/`.

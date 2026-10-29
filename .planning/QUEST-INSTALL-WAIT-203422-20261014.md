# Quest Android wait wrapper and inactive debugger history

Capture: `quest-build-support-20261014T203422Z-7e98bf60.zip`.
Branch: `feature/quest3-standalone`; runtime unchanged at B672 / 1.1.1.

## Evidence

The report identifies Builder `b6a3c8e143061ce7a0fbc7ae64773abd00286e20`,
the same installed B672 APK (`d4e76da00ec2`) and retained `reset-installed`
transaction. The preceding directory-upload repair works: all four mod-bank
files, 245,953,902 bytes, are transferred and committed. The game bank has not
yet completed its transfer in this attempt. No Unity or new APK work is needed.

Automatic storage-view requests still do not start a process on this firmware.
The user subsequently starts the app from the headset library. The Builder
positively observes its fresh pre-Application debugger wait and pauses the exact
UID/PID/starttime. The real JDWP stack then includes:

```
android.os.Debug.waitForDebugger
android.app.ActivityThread.waitForDebugger
android.app.ActivityThread.handleBindApplication
```

The previous guard requires the first and last calls to be adjacent. It rejects
the additional framework wrapper before any storage command. Its cleanup then
explicitly force-stops the owned process. This explains the app disappearing in
the observed installation sequence; it does not prove a game/runtime crash.
The Builder report contains no independent full gameplay crash capture.

After `am clear-debug-app`, dumpsys returns current `mDebugApp=null` and
`mDebugTransient=false`, but keeps `orig=dev.gloomhavenvr.quest` and
`mOrigWaitForDebugger=true`. The old empty-output requirement incorrectly keeps
the durable owner marker and can block the next attempt.

## Repair

Admit only the exact optional private instance
`ActivityThread.waitForDebugger(ActivityThread$AppBindData)` framework wrapper
between the original Debug wait and bind. Verify descriptor, method flags and
bootstrap class ownership. The pre-wait route retains its live `waiting=true`
argument and exact wrapper/outer call-site witness. Actual Suspend_ALL step
events and post-step wait proofs remain mandatory. A false proxy, foreign class,
unrecognized wrapper or Application/LoadedApk/game frame remains refused.

Recognize the owned inactive debugger history separately from an active
selection. Verify empty persistent settings; remove the durable marker only
after confirmed release. On restart, reconcile an already-cleared wait without
issuing another clear. Foreign active selections, persistent settings, unknown
history and changed installed-source identities remain protected. Subsequent
manual starts can arm from the observed inactive history.

Manual-start instructions now explain that this is an installation check and
the Builder closes that temporary process again. A content-access refusal after
successful APK installation is an actionable retained-installation pause with
retry/export/skip options, not another signature conflict, reset or full build.
Do not restore original PC backups after the user's app-data reset.

## Primary source and validation

[Android 14 ActivityThread](https://android.googlesource.com/platform/frameworks/base/+/refs/tags/android-14.0.0_r1/core/java/android/app/ActivityThread.java)
contains the private wait wrapper; Android 13 retains the direct call.
[ActivityManagerService](https://android.googlesource.com/platform/frameworks/base/+/3b58eff/services/core/java/com/android/server/am/ActivityManagerService.java)
clears live debugger values without overwriting the original historical fields
on a persistent clear. Source excerpts and hashes remain in the worker evidence.

Focused checks include actual JVM JDWP transport/invocation, wrapper and direct
paths, true/false proxy controls, exact process cleanup, durable wait restart,
inactive-history rearming, retained content, Wizard failure classification and
real Chrome guidance. A preexisting fixture's unconfigured truthy pidof Mock was
corrected to the actual empty post-termination response; production process
identity checks were not weakened to satisfy that fixture.

Handoff records actual previous-release producer compatibility, the extracted
game-free Builder and fixture/worktree cleanup. No fresh Unity/APK build,
Windows end-to-end export, complete runtime/wire gate or successful Quest
installation is claimed. Android ART and FUSE behavior still require hardware.

Evidence: `/home/claw/quest3-local/build/evidence/B672-install-start-203422-20261014/`.

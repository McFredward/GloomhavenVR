# Quest installation: app-origin storage recovery (2026-10-14)

Runtime remains ModBuild672 /1.1.1. This repair changes the host-side Builder and
installer only; it does not require another Unity import, APK export or new
proprietary game conversion. Keep the Windows `.ghvrq` workspace and resume its
unfinished installation with the corrected game-free Builder.

## Captured failure

`quest-build-support-20261014T165236Z-4b681a94.zip` identifies released Builder
`4ba4d58430255ca47ed09d15e0c2e2d59081654c`. All six earlier stages are complete.
Installation attempt7 pauses after6.187seconds in the existing signing recovery,
whose durable journal is already `restoring`. Its replacement B672 APK has input
`d4e76da00ec22d7a615f5cd16f4e930adda0a0c704c70996536cd39606b2ef9a` and SHA256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.

Authorized USB is working. Android starts PID10415, app UID10190, starttime37449;
the current ActivityThread explicitly waits for its debugger. The installer
verifies that identity and stops all native tasks. Nevertheless, both the direct
`/storage/emulated/0/Android/data` guard and its `/proc/10415/root` alias fail
under `run-as`, returning exit1 without stderr. There is no evidence of a new
offline connection or a sleeping/no-PID launch in this attempt. The exact errno
and contribution of FUSE, SELinux or mount visibility remain unproven.

The original external backup is nonempty:246,323,200archive bytes,36files and
16directories containing246,284,296file bytes. Private and device-protected
archives each occupy10,240bytes. Keep those backups and the installed APK.
The11GB content bank is not this save/settings archive and must not be read,
copied or captured again to repair this access failure.

## Repair boundary

Changing a pathname to `/proc/PID/root` still executes commands from the caller's
context. Add a third access mode that starts a storage-only child through the
actual debuggable app's boot Java Runtime, at its verified pre-Application wait.
This uses a loopback ADB JDWP forward; no app registration or APK patch is needed.

JDWP invocation requires event suspension. Queue the handshake and initial
VM suspension before releasing native SIGSTOP; inspect the unique main thread
and exact Debug/ActivityThread wait frames. Establish a one-shot single-step
event with Suspend_ALL and recheck the wait stack before invoking only boot
`Runtime.getRuntime` and `Runtime.exec`. Use single-threaded invocation, bound
packet/output sizes and keep the hardware/PID/UID/starttime guards. No game
method or Application lifecycle is evaluated. The shell reports completion
through atomic private files; waiting on Java Process from a suspended thread
would risk deadlocking its process reaper.

Stage only the retained sanitized app-data TAR in an exact app-owned private
cache file. Journal that path before uploading any bytes. An interrupted retry
removes only the witnessed scratch file, preventing repeated246MB stage copies.
Extract and qualify the original typed census, file hashes and terminal links
through the actual app-origin view. Do not substitute a shell-readable restored
tree for app readability. Reuse the same bounded seam for retained content open
probes and small current-app files; normal content qualification still samples
only its receipt and representative files.

Terminate the exact owned storage VM before detaching the debugger. Normal
cleanup confirms package process termination; a lost ADB transport may use Exit
on the already authenticated original VM socket. Dispose is never used because
it could resume Application. Unconfirmed cleanup retains the suspended connection
and backup instead of claiming successful restoration. Private scratch cleanup
is scoped to the same app UID and exact generated paths.

Retaining a debugger socket is a protection within the live installer process,
not a guarantee across loss of both transports or host-process termination.
JDWP disconnection can release VM suspension independently of host intent.
Original backups and the unfinished journal remain authoritative across that
interruption; the next connected retry must qualify the current process/view
again. No unconditional firmware shutdown or orphan-child guarantee is claimed.

Distinguish `restore-app-storage-unavailable` from the legitimate no-PID
`restore-app-access` action. The former states that the Quest is reachable and
keeps restoration/retry/log export prominent without misleading USB/wakeup
guidance. Connection loss retains the installer's reconnect action. Completed
build work remains green; the retry targets installation only.

## Validation and limits

Focused shell/tar/hash fixtures cover exact retained data, interrupted staging,
typed census and hash refusals, changed process ownership, and absence of any
full game-bank read. Protocol fixtures include real JVM debugger sessions in
addition to malformed framing and lifecycle controls. They exercise the actual
host protocol implementation while adapting Android-specific paths and UID
observations; they do not establish Android firmware behavior.

The handoff receipts record the exact final sources, focused case counts, real
browser checks and extracted release launch. Compare the complete actual prior
release inventory to retain original recovery/preparation and compact-update
producer scopes. No runtime/wire gate or full Windows game build is claimed.
New Quest restoration and gameplay outcomes require the next hardware test.

Primary protocol reference:
[Oracle JDWP command and event specification](https://docs.oracle.com/en/java/javase/17/docs/specs/jdwp/jdwp-protocol.html).
Compact capture, test, release and cleanup evidence lives under
`/home/claw/quest3-local/build/evidence/B672-install-awake-20261014/`.

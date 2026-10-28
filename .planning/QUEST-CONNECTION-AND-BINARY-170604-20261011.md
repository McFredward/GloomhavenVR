# Quest USB selection and binary completion — capture170604

## Observed failures

`quest-build-support-20261011T170604Z-56a9d1d3.zip` identifies delivered Builder
`c5e05efa8`, Runtime671 and the completed B665 APK. All six preceding build stages
are closed. Both USB and Wi-Fi transports of the same Quest initially appear as
authorized. The previous installer discovers USB but unnecessarily executes
`tcpip 5555`, restarting adbd and briefly losing that transport. Wi-Fi reconnect
succeeds on its second attempt; connection alone does not explain the final stop.

The replacement B665 and restored saves are already verified. The subsequent
112-byte content receipt again encounters Android sync `fchown` EPERM. The old
`exec-in` fallback reports local exit0 but the unique post-EOF marker is absent.
AOSP Android12 `services.cpp` selects raw/no-protocol execution and
`shell_service.cpp` forces a PTY for that combination. Closing the input therefore
can send SIGHUP before the shell writes its completion marker. The preceding
Linux subprocess-pipe controls did not model this daemon PTY boundary.

## Changes

Default installation, log capture and independent device connection prefer an
authorized USB Quest before reconnecting Wi-Fi. They preserve the remembered
hardware identity, select that identity among multiple attached headsets and do
not restart adbd for a cable installation. A cable attached during a failed
reconnect is considered again. Explicit wireless setup remains available; if its
Wi-Fi connection fails and the same headset is still reachable by USB, the
installer uses the cable. USB serials are stored separately from network endpoints.
An explicit hardware change discards a foreign remembered Wi-Fi endpoint.

Missing connection, USB debugging authorization or device selection becomes an
action prerequisite. The Wizard retains the completed APK and progress, shows
USB data-cable/debugging instructions with retry, and hides technical error
presentation for this prerequisite. Actual invalid-file or signing failures keep
their diagnostic cause. German and English guidance explain that cable
installation does not require Wi-Fi.

For the observed sync ownership rejection, a bounded binary client talks to the
already-running **local** ADB server using `shell,v2,raw`. It frames exact binary
stdin, sends CloseStdin and waits for the remote Exit frame. This avoids both the
daemon's legacy PTY and the Windows shell CLI's text-mode CRT stdin. There is no
base64 expansion or whole-file temporary copy. Standard sync remains the fast
path. Remote stdout is drained; retained stderr is capped at16KiB; all reads and
writes share a finite deadline and the writer is reaped on completion or failure.

The destination is published only after the unique EOF marker, exact remote
size/SHA and stable source are proved. No speculative marker polling or second
full-file hash is added. Failed transfer preserves saves and completed content;
cleanup removes only that operation's unique staging. Bank progress continues
to measure actual transmitted bytes and finishes only after verification.

## Validation and delivery

Fresh focused checks: 191 installer/content/collector/binary cases run,190 pass
and1 deliberately skipped;71 affected Wizard cases and61 support/release cases
pass. All82 UI cases pass, including real Chrome checks of the connection
callout, USB instructions and retry/skip actions. These are404 fresh passing
host/UI cases, not a new complete local game gate.

An independent worker checks18 additional private controls: five real PTY/CRT
boundary controls, ten connection-selection controls and three coupled production
installer/local-server/real-shell controls. The latter prove exact binary delivery,
unauthorized-server action classification, and preservation of the previous
destination/save when same-sized bytes fail verification. The16 binary protocol
tests are included in the191-case installer group, not counted again.

Qualify the actual game-free ZIP through its source inventory, extracted Linux
launcher, retained session and old/new delivered inventories at the captured
complete27 preparation frontier. No Unity import, Shader matrix, game rebuild,
new uninstall or APK/bank backup is needed. The roughly10.93GB normal content
redelivery previously accepted by the user is still necessary where bank retention
was refused; it is separate from the small save/settings backup.

Runtime sources retain the preceding strict Release evidence. Actual Quest
content delivery and official Windows end-to-end behavior remain hardware
outcomes. Publish only `feature/quest3-standalone` under McFredward. Replace the
public latest Builder ZIP/audit/validation; reap processes and remove the owned
audit worktree, proof caches and private duplicate ZIP. Preserve original inputs,
current symbols, incoming captures, branch refs and other agents' work. Compact
source-bound evidence lives under
`/home/claw/quest3-local/build/evidence/B671-connection-170604-20261011/`.

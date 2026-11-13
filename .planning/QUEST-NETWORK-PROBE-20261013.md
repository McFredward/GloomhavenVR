# Quest Builder: ADB timeout, local SDK reuse and verified download alternatives

Report `quest-build-support-20261013T183123Z-c683031b.zip` identifies the current
game-free release `b6514bce9`, ModBuild672. Its latest retained run is blocked
before resumed compilation by one dependency probe: Android platform-tools from
`dl.google.com` reports `The read operation timed out` after five seconds.
Thirteen other dependencies are already local; six remote checks succeed. No
certificate-verification error or actual proxy refusal is recorded. AdGuard is
reported by the maintainer but its involvement is not established. Do not tell
the user that general internet access or proxy configuration is broken.

The old five-second one-byte preflight was being treated as definitive proof
that a required download could not work. Classify its timeout as an inconclusive
`probe-timeout`, retain the endpoints/causes, and let the actual producer run
its longer, bounded requests. Actual TLS, DNS and HTTP refusals remain separate
observed failures. Error messages name the affected dependency and failure type,
without guessing that the user must change proxy settings. The current timeout
status is displayed in English/German rather than only as a machine token.

The selected Unity Android SDK can already contain ADB under
`Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools`. The previous
preflight/discovery omitted this exact selected path. Reuse it in both the local
preflight and the installer command (`--adb`), on Windows and Linux. Preserve the
lexical managed-cache path for the existing installer ownership checks. A resumed
conversion may retain older frozen mod sources; installation/mirror policy must
come from the running Builder's source, which owns that host action.

Pinned platform-tools37.0.1 now has three fixed Google download endpoints for
each supported host:

- `dl.google.com/android/repository/`
- `dl-ssl.google.com/android/repository/`
- `redirector.gvt1.com/edgedl/android/repository/` (redirects to Google CDN hosts)

All original version, size and SHA-256 pins remain unchanged. Four complete
actual alternate downloads match those pins, including the redirected CDN bytes.
The actual provisioning entry point also succeeds with a fixture-blocked first
host, and again with both first hosts blocked, for Windows and Linux archives.
No Windows executable was run on the Linux test host.

TLS remains verified on every request and HTTPS redirect. Each host has a bounded
connect/read wait and elapsed transfer limit. Timeout, interruption, malformed
HTTP chunks and wrong bytes discard only that private incomplete transfer and
advance to the next host. Local permission/disk-full errors do not trigger more
network work. Logs report the endpoint and safe failure class without proxy
credentials or temporary CDN query parameters. Existing verified ADB caches
remain reusable and installation does not scan game payloads for this repair.

A global support export now includes each blocked run's current bounded action
and network causes. Historical `build/last-failure.json` remains evidence of its
older compiler invocation; it no longer provides the only visible error summary.
Finished/resumed runs cannot export a stale blocked action as their current state.

Focused checks cover preflight, actual installer arguments, workflow resumption,
device utilities, archive/ADB provisioning, current support summaries and source
compatibility. The real TLS fixtures still reject untrusted certificates. Exact
unchanged original conversion, B672 runtime and parallel-import evidence is
inherited; no new game/APK export or complete runtime/wire gate is claimed.

Compact evidence is retained under
`quest3-local/build/evidence/B672-network-filter-20261013`. The completed fixture
children are reaped and caches removed through `cleanup_completed_run`. Preserve
the maintainer's `.ghvrq` workspace and resume the stopped run with the new
game-free archive; this repair changes no original asset/native cooking producer.

Primary background references:
[Android platform-tools](https://developer.android.com/tools/releases/platform-tools)
and the [Android SDK repository host](https://android.googlesource.com/platform/sdk/+/b0ac962%5E%21/).

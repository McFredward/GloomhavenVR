# Quest cached signing-tool handoff

Capture: `quest-build-support-20261016T105340Z-191295e9.zip`.
Branch: `feature/quest3-standalone`; runtime unchanged at B672 / 1.1.1.

## Observed failure

The report identifies Builder source `6eacb8d9109f` and update session
`7094e22184e1468bb4c20ab0f487b0db`. The completed no-Unity tools stage writes
`sessions/<session>/apk-tools.json`. Its actual producer includes schema1,
the six signing-tool paths and `host=windows-x64`. The update driver's reader
accepts only schema1 and the six paths, rejecting the producer's own host field
with `APK signing tool selection has an unsupported schema`.

The inspect-update child exits after4.24seconds. It has not inspected the source
certificate, published an update plan, modified an APK or installed anything.
Historical build and installation logs in the same support archive are not new
failures in this run. In particular, the Android bank-metadata repair delivered
in the previous Builder has not yet been exercised on the headset.

The existing completed APK is B672/input`d4e76da00ec2`, SHA256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
Keep that APK, its banks, receipts and `.ghvrq` workspace.

## Bounded repair and continuation

Accept optional, explicitly validated Windows/Linux host metadata in the reader,
then remove it before returning the existing six-path tool selection. Continue
to reject unknown fields, unsupported host/schema values, malformed paths and
missing executables. Legacy packets without host metadata remain supported.
Do not rewrite a cached packet or invalidate its completed provisioning receipt.

Add only this exact reviewed driver revision to the finite host-only producer
aliases. The signing metadata reader changes no game assets, native code or
runtime input. Unknown driver revisions must still change the compilation and
preparation identities. This preserves the previous release's preparation,
recovery and compact-code scopes without granting future edits a blanket alias.

After replacing the Builder files, resume the failed update session. Its cached
tools remain eligible; retry the failed inspection. The existing current-APK
retention and completed-installation proofs can then run. Real profile, code,
asset or signing changes keep their ordinary update requirements.

## Validation and limits

The regression runs the actual Wizard provisioning producer and actual update
reader together, using checksum-qualified tiny tool archives and adapted native
version probes. Cover Windows/Linux packets, cached reuse without rewriting,
legacy no-host packets and malformed-input controls. Compare the entire actual
previous delivered source inventory with the new preparation/recovery/code
identities, including a negative control for unknown driver edits.

Run the focused update and Wizard handoff suites, package the game-free Builder,
then smoke-test its extracted Linux launcher and delivered Python modules.
Record exact counts and source/archive hashes in the adjacent validation JSON.
Reuse the previous round's evidence for unchanged Android storage, wireless,
installation and UI code; this is not a new complete runtime gate.

No Unity project import, game build or APK build runs locally. No successful
headset startup, bank access or wireless reconnection is inferred from host
checks. The next hardware run must establish those outcomes.

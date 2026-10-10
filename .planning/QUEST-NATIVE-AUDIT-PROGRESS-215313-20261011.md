# Quest native file audit and Unity phase completion (2026-10-11)

## Captured failure

The supplied `quest-build-support-20261010T215313Z-372b6112.zip` identifies
Builder source `72c97fd2df645813795ec8ef3f8c541481d73440`, Runtime664. Its
preparation receipt closes all27 original owners without a pending transaction.
Unity advances through the repaired loading Sprite and compute source checks,
constructs the native Android Addressables output, then stops in
`actual-native-addressables-before-player` with
`Native Shader audit file changed while being read`.

The reader compares a path `stat()` tuple directly with `fstat()`, including
`st_ctime_ns`. On Windows those APIs can report creation and change time
respectively for the same unchanged file. The original-asset copier already
accounts for that difference. The native audit had not. The platform behavior
is documented in [CPython issue157671](https://github.com/python/cpython/issues/157671).
The old error lacks the offending path and before/after metadata, so the capture
cannot identify the particular Windows file or independently prove its stamps.
No original native bank bytes from that PC are available locally.

The capture also retains complete catalog-entry and Shader-root counters while
the corresponding UI phases remain pending at99%. Completion of their child
counters did not close the method phase. This is separate from the native-file
read failure; a later failure must not turn successful siblings back to pending.

## Preservation contract

This is a repair to late readers and progress observation. Original compute,
Shader, Sprite, texture and content producers remain unchanged. The exact source
profile from the previous delivery remains qualified at the complete27-owner
frontier; unknown producer/consumer edits still acquire a different scope. The
reviewed late Editor scripts update under their existing owners and `.meta`
identities. There is no Unity Library reset, import-cache deletion or forced
Shader recompilation. Unity retains its ordinary incremental Addressables cache;
a fully completed native bank/Player receipt is not invented for the failed run.

The delivery includes published dev665 (`ce637a1dc3fb609204701c701498a20cd7bdc0ae`)
on `feature/quest3-standalone`. No Quest integration is pushed to `dev`. Runtime
behavior, default choices and additive wire contracts come from that published
review. The affected build/runtime compilation and static weaving are checked
locally; published NPC665 gate evidence is inherited, not rerun or reclassified.

## Reader and progress behavior

The reader compares device/file identity, size and modification time across APIs
on Windows. It still compares each API's complete metadata, including its own
change/creation timestamp, before and after the read. It checks the closed path
again and retains final publication checks. Links, replacements, growth and real
metadata changes remain rejected with the path, check and both stamp tuples.
Data is read in bounded8MiB chunks once; it is not scanned again for progress.

The native gate reports actual selected bundle bytes, bundle counts and exact
public Shader/material/collection root counts. The Editor forwards the verified
helper's progress while it runs, drains stderr concurrently and accepts the
completion receipt before closing the audit. Existing native Shader variant
reports remain observed without a second compile. Producer counters are limited
to one update per250ms and the Unity UI polls every500ms; neither cadence
invents work or guarantees continuous movement during an indivisible native
operation. Unity can still emit some compiler results in blocks. The complete
part becomes100 only on a source-proven successful boundary.

The old raw progress logs are tail-truncated: the complete catalog and Shader
terminal events are no longer retained. Tests use the captured counts and the
source-proven existing Counter.Complete calls; they do not claim an untruncated
replay of that Windows run. The browser displays successful children as green,
100%, and gives the subsequent audit its own counted phase. Native Player and
Gradle success close their child parts, while final Player/receipt ownership
stays open until the original caller validates it.

## Verification scope

Final evidence is recorded in the adjacent delivery validation receipt. Focused
reader mutation controls, terminal progress and retained preparation tests must
pass before shipping. Actual native-file reads and production Editor counters
are exercised independently of a full game import. Windows timestamp behavior
is represented by explicit cross-API fixtures, not a claimed native Windows run.

A fresh game import, opt-in Shader compiler matrix, complete Windows APK and
headset outcome are not performed or claimed in this repair round. The user
builds on Windows; the resulting full APK remains the next acceptance gate.

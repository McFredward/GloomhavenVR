# Quest Android census repair — capture155522

## Evidence and scope

`quest-build-support-20261011T155522Z-46095b36.zip` identifies the delivered
`98de0f11` Builder, the completed B665 APK, and six closed build stages. The
installation retry stops before uninstall or backup transfer. Its private
`find` result includes the app-data root itself, which correctly fails the
descendant-only census boundary. The existing app and saves remain installed.

The old command combines `-mindepth 1` with an ungrouped prune/OR expression.
GNU find treats mindepth as a global option; Android Toybox evaluates it as an
expression. It therefore applies only to the first OR branch. Independent
controls compiled from the official Android12 Toybox source reproduce the root
entry and verify the grouped replacement. Host GNU-find success was insufficient
evidence for the Android command.

## Changes

- Group the complete prune/output action after `-mindepth 1`; use `-print0` and
  require complete NUL framing. Foreign paths, parent traversal, CR/LF filenames,
  empty entries and malformed output still fail before destructive work.
- Prune internal caches before traversal. The procedural engine obtains Android
  `Context.filesDir`, so its generated prefix lives below
  `files/quest-procedural-state`; also retain the legacy top-level spelling.
  Prefix/cache/tmp/home payloads are regenerable. Preserve worker diagnostics,
  unknown sibling settings and all native save/history/import roots.
- Prune external Android cache and exact IL2CPP extraction filenames found in the
  selected APK's central directory. Read directory metadata only, never APK
  payloads. Preserve unknown Metadata/Resources siblings and `unity.ver`.
  Escape find's fnmatch metacharacters independently of shell quoting, so an
  unusual packaged filename cannot exclude a differently named user file.
- Reject an obsolete retained archive through its recorded member names/types/
  sizes before hashing or reading it. Healthy small archives still require their
  existing integrity qualification before reuse and before uninstall.

The older external census explains the apparently oversized app-data archive:
101 readable files total201,498,408 bytes. Fourteen regenerable asset-bank files
account for165,191,339 bytes, eight Unity extraction files for35,415,329 bytes,
and the remaining settings/save/log files for891,740 bytes. Seven extraction
filenames/sizes match the canonical B625 APK's central directory exactly; the
eighth is36-byte `unity.ver`, which remains preserved. The current APK determines
which exact extraction copies may be omitted. This is not a fixed save filename
allowlist and does not assume later hardware will have the same byte count.

Existing save-only migration and its scoped content-reinstallation choice remain
in force. No old APK backup, game-bank copy fallback or full asset hashing is
introduced. If Android refuses direct directory rename, the user chooses before
uninstall whether the normal installer may retransmit game content.

## Validation and delivery

Fresh focused checks pass:109 installer/signing cases,69 Wizard retry/consent/
delivery cases and61 support/release cases (239 distinct host tests). The final
source also passes23 independent Android Toybox controls, including four literal
resource-filename variants. An initial new fixture referenced a nonexistent
adapter field; it was corrected and the entire affected installer subset rerun.

Focused installer/signing tests cover real filesystem migration, interrupted
recovery, obsolete archive rejection, protected saves and unknown settings,
malformed census boundaries and sparse generated payloads excluded before stat
or read. Existing Wizard retry/consent/completed-APK and support-export scopes
are rerun as applicable. Independent controls use actual Toybox find/tar and
the production `Adb.run` NUL transport boundary. They run on Linux x64; they do
not establish Quest UID permissions, Android daemon behavior or a complete
Windows installation outcome.

Release qualification verifies the actual game-free Builder archive, its dynamic
modules, an extracted Linux launcher and retained-session selection. The prior
delivered source inventory is compared with the new inventory at the captured
complete preparation frontier, with changed-producer negative controls. The
finished B665 APK must remain selected and its six closed stages must stay
closed. No Unity import, shader matrix, runtime rebuild or fresh APK export is
required. Unchanged game/UI checks retain their previously recorded evidence;
this bounded host repair is not a new complete game gate.

Compact logs, source-bound audit receipts and delivery validation live outside
the public handoff directory under
`/home/claw/quest3-local/build/evidence/B671-install-census-155522-20261011/`.
Publish only the latest Builder ZIP/audit/validation on
`feature/quest3-standalone`; remove this round's owned worktree and generated
proof caches after reaping their processes. Preserve incoming captures, canonical
inputs, current symbols, branch refs and other agents' work. Successful hardware
migration remains unverified until the maintainer tests this delivery.

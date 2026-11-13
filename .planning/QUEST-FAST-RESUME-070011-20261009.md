# Quest retained-result observation — 2026-10-09

## Supplied evidence and limits

`quest-build-support-20261009T070011Z-233f794a.zip` identifies the published
`07ebc472d3b68d1f8bb4faa7f60afa575d548bbb` Builder / ModBuild647. It captures
an active run, not a new final build failure. Opus succeeds, `native-runtime`
and `bundled-audio` commit, then `native-cubemaps` begins.

Original selection and snapshot checks already reuse all5227 original files
with zero payload reads. The outer recovery receipt likewise reports191415
metadata hits and zero rechecked bytes across32551645233 output bytes. Those
per-file Windows handles, ancestor walks and database calls still create a
large retained-result operation even though the bytes are not rehashed.

Preparation then starts with zero committed contracts and executes the base
copy and all eleven startup-content substages again. This is real repeated
preparation, not only a backwards display. The support archive does not contain
the old/new complete input manifests or preparation journal. It therefore cannot
establish the exact input/producers mismatch that caused this particular reset.
The truncated progress middles also do not isolate a precise recovery-versus-copy
wall-time split. Do not present either as measured causality.

## Bounded repair

Closed outer stage receipts, preparation contracts and derived staging journals
use `ValidatedFileWitnesses.qualify_many`. It fetches the owned witness records
once and observes files in guarded directory groups. Receipt SHA/size, namespace,
real file identity and change stamp stay authoritative. A changed, legacy,
unknown-driver or corrupt witness falls back to the existing per-file reader.
Missing files fail qualification. Directory mtimes never prove child content.

Windows metadata batches use the documented
[FILE_ID_EXTD_DIR_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_extd_dir_info)
via
[GetFileInformationByHandleEx](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex):

- `CreateFileW`: access`0x81` (`FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES`),
  share`7`, `OPEN_EXISTING`, flags`0x02200000`
  (`FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT`).
- Directory basic attributes reject reparse points and non-directory handles;
  volume-qualified identity is supported only for NTFS/ReFS.
- Explicit Windows-width entry header88bytes; FileId offset72, UTF-16 filename
  follows the header. Fields retain128-bit FileId, true ChangeTime,
  LastWriteTime and EndOfFile, matching established`win32-v1` proof stamps.
- Class20 restarts enumeration, class19 continues in64KiB pages. Only
  `ERROR_NO_MORE_FILES` closes a successful enumeration. Invalid/unsupported or
  partial structures never become accepted metadata.
- ReparsePointTag is ignored for ordinary entries, as documented: its value is
  undefined unless FILE_ATTRIBUTE_REPARSE_POINT is set. A nonzero unused tag
  therefore cannot silently force ordinary files into the slow fallback.
- The directory structure has no link count: leaf`lstat` remains required.
  Hardlinked files retain conservative individual byte checks; staging still
  enforces its stricter one-link writer rule. Leaf reparse points, linked
  ancestors and a replaced owned root remain rejected.

This is metadata observation, not a new asset producer. The exact reviewed
four-helper`OBSERVATION_DIRECTORY_BATCH` profile preserves existing preparation
and derived-stage source equivalence. It accepts only the complete exact byte
set; mixed or unknown helpers remain distinct. During a proved key migration,
the accepted previous strong stamp is transferred unchanged, rather than pairing
an old SHA with freshly sampled metadata. Immutable sources, source changes,
mutable archive ownership, unfinished rollback and later phase owners keep their
existing independent gates.

## Verification

Focused suites: storage witnesses46, preparation42, derived staging17,
recovery continuation55, preparation identity13, package pipeline22; all195
pass. Windows ABI/paging controls and a real-file fixture with modeled Windows
responses cover Unicode, true ChangeTime, changed ID, unsupported metadata,
linked paths, hardlinks, missing files and bounded changed-file fallback.
These are portable tests, not execution of the new Win32 API on Windows.

An additional real Preparation pipeline fixture reads the exact previous
published helper bytes from07ebc472d. After updating to the current reviewed
profile it keeps all14 closed receipts byte-identical, repeats no closed
producer/base copy, retains Unity Library and retries only native-cubemaps.
The unfinished transaction's original bounded undo is intentionally restored.
Original codecs and compilers use small declared adapters in this proof.

On the actual Linux filesystem, an8192-file/64-directory warm fixture takes
0.6525s with individual qualifiers versus0.2434s batched; both read zero payload
bytes. Its generated files are removed after recording the result. This is not
a Windows ETA or an end-to-end build speed claim. Windows whole-build duration,
retained-result latency on the maintainer's disk and headset behavior remain
unverified. No Unity import, APK build or shader audit was repeated.

Compact evidence remains outside the handoff directory at
`/home/claw/quest3-local/build/evidence/fast-resume-070011-20261009/`.

# Fast outer Wizard receipt qualification — 2026-10-09

## Additional repeated-read boundary

The Builder already owns persistent byte-qualified witnesses for recovered and
prepared content. The outer Wizard has independent schema-1 stage receipts.
Before this change, `Store.valid()` reads every recorded output again on every
resume, including tool files, APKs and delivery banks. Builder-level reuse cannot
prevent those outer reads.

This repair changes only the outer Wizard stage-output proof mechanism. It does
not remove the original owned-game qualification, weaken Builder dependencies,
skip failed work, or alter the selected source/game/profile keys.

## Implemented behavior

`state.py` loads the selected Builder's standard-library
`ValidatedFileWitnesses` implementation under the private
`_ghvrq_wizard_receipt_storage` alias. Application `storage` imports remain
untouched. Each session retains a small derived witness database at:

```text
<wizard-workspace>/sessions/<session>/receipts/.file-witnesses.sqlite3
```

The witness owner includes the exact workspace, session, stage and stage key.
Publishing records actual output digests and their strong change stamps. Warm
validation uses the existing `qualify_many()` directory batches and indexed
SQLite evidence instead of opening every SDK file's attributes independently.
The authoritative receipt format and output hashes remain unchanged.

Windows witnesses use real volume/file identity and ChangeTime, distinct from
Python's path creation time. Supported directory batches preserve the full
128-bit file identity. POSIX witnesses use device, inode, size, mtime and ctime.
The batch still guards the owned root and directories and checks each leaf.
Changed/missing evidence falls back to actual bytes; an unexpected digest rejects
the receipt. Hardlinks and unsupported metadata/filesystems retain conservative
byte qualification. Links, reparse paths, escaping paths, unsafe witness files
and malformed receipt hashes are refused.

## Legacy migration and transparency

An old schema-1 Wizard receipt has no strong output witnesses. It therefore
requires **one initial actual byte qualification** against its existing hashes.
Those hashes are never accepted from size/mtime alone. Successful qualification
seeds persistent witnesses without rewriting the old receipt or resetting build
progress. Subsequent unchanged resumes require metadata rather than payload
reads.

Before a needed initial byte read, the progress log explicitly reports:

```text
First qualification of this legacy receipt: one-time byte read; later resumes use strong metadata.
```

A changed or unqualified output under an existing witness owner is described as
requiring actual byte qualification. The final report gives metadata hits and
the number of files and bytes actually reread. A fully warm receipt reports
`0 files / 0 bytes rechecked`. Progress byte counters represent the qualified
payload extent; the explicit summary distinguishes that from physical reads.

Corrupt witness rows lose trust and are requalified. A derived SQLite database
identified as corrupt/not-a-database during opening is discarded and byte-seeded
again after ownership guards; actual outputs and authoritative receipts survive.
Unrelated SQLite/I/O errors never become successful qualification. Cancellation
continues to stop both cold hashes and warm metadata iteration.

The current supplied Windows run is still inside the Build stage and has not
published a completed outer APK/delivery-bank receipt. This repair must not be
described as skipping a nonexistent old 12-GB outer receipt in that capture.
The separate Builder witness batching work covers its retained approximately
32.55-GB content. This layer prevents additional outer rereads when such stage
receipts actually exist.

## Focused evidence

- 20 new receipt-resume tests cover publish digests, true one-time legacy
  migration, warm new-Store and fresh-process reuse, altered bytes with preserved
  size/mtime, replacement identities, missing outputs, foreign keys/stages,
  malformed hashes, corrupt witnesses, links/reparse/hardlinks, unsupported
  stamps, Windows ChangeTime, cancellation and mutation during publication.
- A real 512-MiB sparse APK fixture is byte-hashed at publication. A separate
  fresh Python process then validates its receipt while both payload opening and
  hashing are forbidden; zero payload reads are required for success.
- A 200-file output receipt verifies warm directory batching while every
  individual fallback qualifier and payload reader is forbidden.
- Existing affected Progress, Wizard, retry-overview and release/support suites
  validate the original lifecycle and unchanged receipt/source compatibility.

No Unity import, shader audit or APK build was run for this storage-boundary
repair. Portable tests include real POSIX metadata and a bounded Windows API
model. They do not establish actual NTFS workstation timing, a completed Windows
game build or headset acceptance.

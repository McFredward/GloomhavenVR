# Quest Builder automatic workspace cleanup (2026-10-19)

The maintainer requested one-click reclamation while preserving fast updates,
and an unambiguous confirmation step for other deletion policies. This changes
the host Builder only. The game remains B710; no runtime patch or new APK is
required to use the new storage page.

## User-visible behavior

Storage management starts with **Clean up automatically / Automatisch
aufräumen**. One click selects and deletes only proven obsolete owned data,
with live activity, cancellation and actual reclaimed byte/file totals. It
does not require the full disk-usage inventory first. Merely opening the page
does not delete anything.

Advanced deletion policies remain separate explicit choices. Their preview
appears outside the collapsed advanced tools, with a size/count summary,
retention consequences and a prominent confirmation button. The potentially
long file list is an optional detail below that button. Closing a preview
does not delete files or permit a stale poll to reopen its confirmation.

## Ownership and update reuse

The safe policy uses finite producer receipts and per-run dependency records,
not file age or arbitrary name patterns. It can reclaim obsolete completed or
retired-run source/project/recovery data, proven duplicate snapshots, obsolete
successful update APKs and exactly recorded abandoned export temporaries.
Run records and diagnostic logs remain available. Current project and source
owners, unfinished-run dependencies, current APK/content locators, compiler
projects, native Player inputs, independent asset/movie/audio caches, tools,
signing keys, savegames and device signing-recovery backups remain protected.

Missing or ambiguous ownership retains the affected data. Symlinks, borrowed
hardlinks, `.keep` pins, changed preview witnesses and active producer locks
keep their existing deletion protections. Metadata walks bind only selected
candidate trees; the cleanup never rehashes the game's large payload files.
Explicit destructive cache removal still warns about rebuilding affected
components and requires confirmation.

Authenticated POST `/api/storage/auto` accepts only an empty object. The server
uses the `safe` policy and additionally checks its explicit `preserved` update
impact before executing the retained selection. It shares admission,
cancellation and error/result reporting with manual cleanup. It cannot consume
a client-supplied path or destructive manual plan. A background inventory yields
cooperatively; active builds/device transfers/package exports remain excluded.

## Validation scope

Evidence is retained under
`/home/claw/quest3-local/build/evidence/quest-storage-20261019/`.
Focused filesystem, HTTP and actual Chrome UI checks exercise the changed
cleanup and confirmation paths: 161 filesystem/storage checks, 29 HTTP checks,
and 62 UI checks. Integration reruns cover the affected safe/manual cleanup,
actual HTTP deletion and final retained-file rendering; passing unchanged
worker checks remain inherited. The distributed archive is separately checked
through its extracted launcher/API. Runtime, original-content producer and
update classifications are compared with the previous B710 delivery, so a
Builder storage change does not claim a new game compilation or asset import.

These are host software checks, not a new Windows Unity build or Quest hardware
result. The previous B710 runtime evidence remains inherited for unchanged game
code; no new complete multiplayer gate is claimed.

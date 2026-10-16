# Quest update: recover Windows metadata access and give actionable choices

## Evidence and actual failure

The corrected report is `quest-build-support-20261016T150224Z-5af82878.zip`,
SHA256 `7278e946fadb5f6017e954bd79eb3404f988c3bc926aab520276ff04136db0e1`.
It identifies Builder `754cfc456f130af74830112c860249bdf86a414f`, runtime B673.
The new run `52dbea2e94864a6786f417f4adae8109` is blocked with zero attempts in
all seven stages. Its original workspace summary contains the exact refusal:

`[WinError 5] Access is denied: latest-session.json.<uuid>.tmp -> latest-session.json`

The report's top-level UGUI `buildFailure` and detailed compilation logs belong
to the earlier run. The primary initially missed the new run's summary and asked
for already-present information. The supplied user text confirms that summary.
Do not classify the old UGUI message as a fresh failure of its repaired path.

The latest-session pointer only selects a default in homepage discovery; the
selected session, its choices, ownership checks, receipts and APK have separate
durable storage. Its replacement before Engine._run stages must not prevent
otherwise valid work. WinError5 proves an access refusal, not which program or
attribute caused it. Read-only and short sharing conflicts are handled as
possible observed conditions; no antivirus, proxy or ACL cause is asserted.

## Automatic recovery

Normal atomic publication remains one flushed temporary file and os.replace,
with no permissions change or extra scan. Windows access/sharing refusals may
retry the same closed temporary payload with five delays totalling1.55seconds.
After an actual refusal, an ordinary single-link JSON leaf beneath the exact
Wizard ownership marker may have its positively observed Windows read-only
attribute cleared. Recheck its identity before that leaf-only operation. Never
unlink the previous document, alter ACLs/folder permissions, follow links or
request elevation. Disk-full and unrelated errors retain their original handling.

If latest-session.json remains busy, Store.remember_session records the precise
deferral and continues the selected run. It does not waive required session
state/receipt writes or change ownership, signing, original game or output
qualification. A later attempt can publish the restart hint when access returns.
Do not repair this by deleting .ghvrq, changing the APK or repeating conversion.

Mandatory metadata that remains inaccessible raises workspace_metadata_busy,
with the exact path, original cause, retry count and retained-work facts. Optional
temporary cleanup/logging failures cannot mask the primary outcome. Bounded
metadata-repair.jsonl records contain paths and actions, never document contents.

## User guidance and diagnostics

The browser presents mandatory metadata waits with the actual cause and path,
specific instructions, retry of the same saved run and diagnostic export. Known
current update problems offer concrete input/signing/APK/project guidance and an
input-review action that preserves selected values without starting another run.
Changing a signing identity, resetting data or choosing a full build remains an
explicit separate user decision. Unknown faults do not trigger an endless retry
or a guessed destructive repair. Running/completed attempts clear stale actions.

Pre-stage exceptions now retain their structured cause and traceback in the
progress log and independent request log. If a mandatory state file is itself
unwritable, the live request error and independent log still retain context.
Home support exposes the newest active run issue directly, including failed
run actions and the metadata repair log, rather than relying on a historical
last-builder-process record. Existing redaction and bounded metadata rules apply.

## Validation and limits

Focused checks exercise real atomic file writes and the real seven-step Engine
under injected transient/permanent Windows refusals. Positive read-only repair,
same-payload retries, persistent optional-pointer continuation and later recovery
join negative controls for mandatory writes, old-document retention, linked or
hardlinked/unowned paths, Linux denial and disk full. Actual background-server
exceptions, typed recovery parameters, home support and cleared current issues
are tested separately. Browser/HTTP tests cover retained retry and input review
without backend mutation, including unchanged installation consent/memory waits.

Exact counts, source hashes, inspected screenshots, extracted-delivery checks and
cleanup receipts are recorded under
`/home/claw/quest3-local/build/evidence/B673-update-resilience-20261016/`.
Windows errors/attributes are simulated on Linux; no actual Windows lock repair,
full Windows update, new Unity/APK build or headset outcome is claimed. B673
runtime/native/rendering evidence remains inherited from its earlier round.

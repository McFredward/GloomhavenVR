# Steam Link stream-quality lease controls

```sh
python3 -B tests/steamlink-stream-quality/run.py --output /private/evidence
```

This bounded suite strictly typechecks the actual new class against net472 and
executes its injected asynchronous monitor/protocol logic on net8. It never
starts a process, OpenVR client, settings write or headset action. Optional
`--native-transcript <file>` arguments parse the exact C helper's captured flat
JSON serializers; the suite copies/hashes those borrowed inputs before use.

Controls cover genuine Frame/Steam Link/native-MR eligibility, rounded/clamped
persisted width, fixed helper request bounds, exact acquired/restored/error
receipts, actual readback clamps and preserved larger manual widths. Lifecycle
checks exercise pending acquisition, reset/re-entry, stale receipt refusal,
width changes, delayed restoration, no overlapping helper generations, no active
lease expiry, bounded retries and permanently blocked reacquisition after any
unconfirmed restore. A controlled post-Start monitor/cleanup fault retains
explicit unknown ownership instead of adopting it as a fresh settings baseline.

The production backend keeps stdin open for the active lease. Explicit release
closes it on a worker, allowing the helper to restore only still-owned values.
The5s grace/possible emergency kill starts after explicit EOF; it is not an
active lease timer. A reset during pending startup cannot publish its stale
receipt, but restoration may wait for the bounded5s startup-readiness window.
Driver setting readback is logged separately from requested width and never
called proof of negotiated or decoded video resolution. An unconfirmed restore
latches unavailable until a new game process rather than retrying a dirty
baseline. Camera/head/loading/menu transitions do not change a stable eligible
lease.

Process invocation/pipe behavior and actual public API effects are not proven
by these monitor tests. The isolated helper's own typed API/rollback/EOF controls
are separate evidence. Each child is bounded/reaped before the central cleanup
API and exact owned compiler-cache removal. Failed runs, sources, transcripts,
logs and results remain retained; no full presentation suite is repeated.

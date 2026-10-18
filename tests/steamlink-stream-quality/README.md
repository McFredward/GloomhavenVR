# Steam Link stream-quality lease controls

```sh
python3 -B tests/steamlink-stream-quality/run.py --output /private/evidence
```

This bounded suite strictly typechecks the actual new class against net472 and
executes its injected asynchronous monitor/protocol logic on net8. It never
starts a process, OpenVR client, settings write or headset action. Optional
`--native-transcript <file>` arguments parse the exact C helper's captured flat
JSON serializers; the suite copies/hashes those borrowed inputs before use.

Controls cover genuine Frame/Steam Link/native-MR eligibility, actual-carrier-derived
width rounded upward, extended helper request bounds, exact acquired/restored/error
receipts, refusal of observed smaller readbacks and preserved larger manual widths. Lifecycle
checks exercise pending acquisition, reset/re-entry, stale receipt refusal,
width changes, delayed restoration, no overlapping helper generations, no active
lease expiry, transient unknown XR extents without reconfiguration, bounded retries and permanently blocked reacquisition after any
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

The Build701 acquired receipt optionally includes a bounded four-bit
`previous_absent_mask`. It distinguishes an absent original key from an effective
false/zero baseline, validates matching placeholders and cannot authorize a
larger inherited manual width from absent policy fields. Actual missing-setting
and detailed API-error C transcripts are included in the focused integration.

Build702 removes the old fixed3072 request. The separate persisted width is inert;
only a valid current carrier extent establishes a width generation. Unknown initial
extents do not acquire anything. Temporary unknown extents preserve an eligible
active generation, but Off/session end/Reset prevent its reuse. Request validation
is64..16384 in64-pixel steps; these arithmetic bounds are not decoder capabilities.

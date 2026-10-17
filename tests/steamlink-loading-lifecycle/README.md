# Steam Link loading lifecycle controls

Run the bounded source-linked test with:

```sh
python3 tests/steamlink-loading-lifecycle/run.py --output .planning/debug/steamlink-loading-lifecycle
```

`--source-root /absolute/checkout` selects another production checkout. The runner
pins/copies the complete actual lifecycle and extracts the production `Monitor`,
`Snapshot`, `Reason`, `Classify` and metadata validator verbatim. An injected
asynchronous reader exercises the real epoch, cancellation, rate-limit and
publication logic. Unity cameras, XR device notifications, VR session identity
and time are explicitly named boundary ports; there is no fake always-ready
monitor. No native reader, headset, Steam process or receiver is executed.

The controls require camera absence, replacement and temporary disablement during
repeated loads to retain a proven HMD/transport identity without another child
query. Initial lookup must work before a camera exists. Real HMD changes and
session/platform/runtime eligibility loss must revoke identity, cancel stale
reads, unsubscribe/restart correctly and reject late positive results. Controller
events must leave identity alone. A causal variant restores the previous camera
coupling and must fail at the precise semantic regression assertion after a
successful compilation. Compiler failure never counts as the expected refusal.

All subprocesses are killed/reaped before `cleanup_completed_run` and the exact
private cache removal. Copied source, pins, logs and JSON results remain. This
suite proves lifecycle decisions, not the rendered image; actual Unity source
output/presentation raster tests and headset confirmation are separate evidence.

# Remote active-card dismissal proof

`scripts/check-active-dismiss-remote-runtime.py` extracts and compiles the actual
production stage receiver, counted actor/seat gate, panel parent/glide/freeze,
retained-original reservation, native destination check, exact-origin composition,
semantic pending loop, first drawable handover and burn release/pose kernels.
Source hashes and every generated compilation input are retained with the run.

The native-model, transform/time, controller and artwork adapters are explicit in
`Boundary.cs`, `FlightBoundary.cs` and `BurnBoundary.cs`. The fixture executes the
production decisions and writes; it does not execute Unity's renderer, the complete
native burn watcher or actual network delivery. Caller-order bindings ensure the
tested reservation precedes furniture hiding and bypasses both ordinary parent and
glide writes during delayed native active membership.

The scenarios cover an intermediate staged pose, native destination reordering,
missing staging snapshots, original actor/card/count safety, hidden berth and
stopped old glides, artwork waiting, more than two seconds of native replication
delay, focus/controller changes, recovery/session retirement and once-only native
burn completion. All 34 causal controls must compile and fail for their named
behavior; compilation failure is never a passing control. `--case` deliberately
produces partial evidence for a bounded repair. The unfiltered invocation is the
registered local and CI suite.

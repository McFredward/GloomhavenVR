# Native first-picture638 dependency challenge

This fixture compiles actual production CaptureCore, delta/bundle/fragment code,
ExtrasSendScheduler, NetAvatarDriver.QueueTownService/ApplyTownServices and
TownServiceMirror.TickRemote. It is deliberately distinct from the632 test: an
owner has completed an earlier real full original through the send queue, while
a cold observer lacks that named baseline. The current original is captured at
15Hz with the numeric-motion subscriber enabled throughout delivery.

Fourteen rows use the shipped enhancement prefab's 26-node hierarchy, native
sprite/material descriptors and original serialized layout exported by the632
exporter. Thirty smaller native original text partitions form 44 members. Native
templates are warm on the observer to isolate named *owner-baseline* delivery;
no final clone or received baseline is fabricated. The actual receiver queue,
including its main-thread coalescing and baseline handling, is exercised.

Run against a candidate source tree:

```bash
python3 scripts/npc-first-picture638-runtime/run.py --source-root /path/to/candidate
```

Retain the unmodified 637 failure as a causal control:

```bash
python3 scripts/npc-first-picture638-runtime/run.py --source-root /path/to/unmodified637 --expect-baseline-stall
```

`--expect-baseline-stall` expects the exact visibility-bound assertion to fail.
It does not certify acceptable production latency. The result retains source
hashes, compiler/runtime logs and first assembled packet versus first actual
complete active original picture, including the received inventory's named
baseline. A successful repair must pass the normal invocation; a control that
merely produces another exception is rejected.

The scheduler runs with a deterministic 50ms send clock; engine waits, native
capture, validation and original clone creation run in real Unity 2021.3.5. The
TMP atlas/shader is equivalent editor content, not the game's binary font atlas.
Measured rows are approximately 8.3KiB, not the earlier hardware's 12.5KiB. The
fixture models a new or rebuilt observer and the same missing dependency shape
as an unreliable lost baseline; it does not establish why a specific hardware
packet was absent, packet loss probability, a socket RTT, headset pixels or a
four-player hardware result.

Two additional focused cases exercise actual dynamic publication and delayed
original-template readiness:

```bash
python3 scripts/npc-first-picture638-runtime/run.py --case lifecycle --source-root /path/to/candidate
python3 scripts/npc-first-picture638-runtime/run.py --case template --source-root /path/to/candidate
```

The historical lifecycle control also selects the unmodified source root and
uses `--case lifecycle --expect-baseline-stall`.
In the delayed-template case, only template-bank availability is the fixture
boundary between the owner and observer in one Unity process. Actual native
admission first blocks after the complete original assembles. `ResolveTemplate`
then supplies the real exported original; the same source sequence must admit
within 300ms, without receiving a later periodic complete repair. Scheduler send
time and real engine time remain separate in every receipt. The retained
[`evidence.json`](evidence.json) identifies exact tested production/fixture hashes
and expected historical failures; see the English failure audit in `.planning`.

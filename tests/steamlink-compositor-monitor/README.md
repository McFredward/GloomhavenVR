# Steam Link compositor observation

This focused fixture executes the production Debug-only observer owner and strict
JSONL parser, plus a net472 typecheck. It never launches the native helper, calls
OpenVR, changes settings, or accesses a headset. The optional native transcript is
explicitly a host-fake observation, not headset evidence.

```sh
python3 tests/steamlink-compositor-monitor/run.py --output /absolute/private/evidence \
  --native-transcript /absolute/host-fake/transcript.jsonl
```

Coverage includes failed launch without per-tick retry, Off/Reset/cooperative EOF,
late acquisition without overlapping workers, budget exhaustion without an MR
cutoff, unavailable values, bounded/strict scalar input, and shutdown failure.
Four compiled negative controls exercise the Debug gate, retry suppression,
unavailable-value handling, and cooperative stop. This is diagnostic observation;
it does not repair image sharpness or dashboard composition.

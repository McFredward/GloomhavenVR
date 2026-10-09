# Native locomotion clocks

`python3 scripts/check-locomotion-clock-runtime.py` compiles the unchanged production
world-grab, turn, scroll arbitration and diagnostic code against real Unity
2021.3.5f1. It runs in Play Mode after Unity reports `timeScale=0`, `deltaTime=0`
and a positive `unscaledDeltaTime`. No fixture defines `Time`, replaces a movement
calculation, or writes the expected output transform.

Tracked device samples, configuration, game mode, notification sinks and the
unrelated scenario tilt/clamp policy are declared fixture ports. The actual
production gesture ownership, deadzones, exponential smoothing, turn pivot,
scroll latch and Debug guards execute directly. Three independently reverted
clock expressions must fail their corresponding paused-motion assertion. A
compilation error or unrelated assertion is never a passing causal control.

The proof covers paused comfort motion and preserved ownership semantics. It
does not establish that the Build653 user's intermittent translation freeze was
caused by a Unity clock pause. New phase-labelled diagnostics record that missing
hardware evidence without changing any gate or pose.

`--case production` or a named old-source control supports focused repairs. Run
receipts retain production/fixture hashes, source copies, native Unity logs and
results; the imported Unity project is removed after the run.

# Short-rest terminal artwork continuity — build 532

## Evidence and limits

The supplied owner logs are build 531. Remote logs remain historical build 500.
The owner reports a brief return to original artwork near the end of short-rest burns;
other burns appeared intact. `LogOutput.log:1211` reports FlameStrike lost and latched,
no live handle, raw GreyOut 0, followed by a native settle and a 2.66-second hold.
The second short rest (`:1944`) ends at GreyOut 1 after 2 seconds. Both offers were
redrawn from Reviving Ether to FlameStrike. These records establish inconsistent
terminal output, not the exact rendered frame or a second native burn start. The
bounded burn-continuity diagnostics do not occur in the supplied files.

## Source defect and fix

`CardEffects.BurnCardTimeline` terminates from elapsed global-clock time but accumulates
paint from delta time. Its terminal step only clears `coroutine`; it does not write
an endpoint. The mod wrapper restored raw shader channels before every step, including
that final `MoveNext(false)`. Its subsequent spent-floor restoration used `nativeStep:
running`. On completion the native handle was already clear, so the floor was not
reapplied. A spent card could therefore expose less-spent raw artwork until a later
policy settle. This particularly affects short-rest cards, whose discarded floor is
retained throughout the preceding burn. Coarse clock progress is a reproduced boundary
condition, not a clock jump proven by the supplied logs.

Retain the same original spent channels through the terminal step while the burn latch
remains set. Do not restart the animation, manufacture completion progress, change the
native iterator's lifetime, or alter gameplay. A legitimate deferred activation reset
clears the latch and remains clean. Real Lost-to-Hand/Round recovery still clears the
floor and original FX before local display and owner publication. Remote appearance
continues to sample the owner's original materials without a separate corrective ramp.

The historical `BURN RAMP ABANDONED` token remains, with corrected wording: low raw
paint cannot alone establish cancellation or the visible picture. This report retains
its existing bounded normal-level behavior. No logging level or rate was increased.

## Validation

The burn-replay harness now extracts the actual spent-channel capture/restoration
methods and uses the production `SpentBurnContinuity`; these paths were previously
stubbed out. Real wrapper tests cover terminal raw progress 0, .1, .5, .97 and .999,
all three retained shader channels, subsequent local/network sampling, genuine recovery,
and deferred activation reset. A planted old `nativeStep: running` fails the terminal
pixel-state assertion; an unconditional floor fails the activation-reset assertion.

Focused result: 633 runtime assertions, six source bindings, 24 negative controls
(including three disconnected publication/local bindings). The integrated gates and
actual headset outcome remain the integrator's responsibility; passing these tests does
not establish the headset image.

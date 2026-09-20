# Short-rest report verification — build 536 round

## Capture provenance

The new files were supplied on 2026-09-20, modification time 17:30:44 local time.
LogOutput.log has 3,604 lines; Player.log has 8,743. Both startup banners identify
ModBuild 534 / assembly 1.0.5.0, the released 1.0.5. The current dev starting point
is 8510c9d2, ModBuild 535 / version 1.0.6. The supplied capture therefore does not
contain the last short-rest correction. Peer logs remain historical build 500;
no new screenshot accompanies this report. The maintainer was asked whether a
separate build-535 test exists; that cannot be inferred from these files.

## Burn evidence

LogOutput.log records ShieldBash on the original fx -59554 / plate -61012:

- 3509–3510: one original starts inactive and advances to raw progress 0.006.
- 3527: at 0.021 seconds, grey/flow 1 and dissolve 0.646 preserve its spent look.
- 3536: the LostMode request is held at 0.664 seconds; the spent floor still exists.
- 3540–3542: at 0.679 seconds the same original plate exposes raw grey 0.343 while
  the renderer has no bound material; at 0.686 seconds that same material is bound
  with raw grey 0.351. There is no recorded second native start or reset.
- 3546–3547: the original completes at 2.006 seconds, raw 1; the card then flies
  to the Burnt pile after its two-second hold.
- 3560: presentation ends at 2.017 seconds with continuityChanges=0.

This repeats the build-534 spent-floor loss analyzed in BURN-535.md. It does not
establish failure of the build-535 fix. The existing correction makes ordinary
sampling follow actual native playback while the original face is inactive;
its regression reproduces the previous defect and preserves raw progress,
completion and legitimate card recovery.

## Decision

Keep the tested build-535 correction in the new development build and do not add
another speculative burn change based on an older binary. Hardware validation
must use the forthcoming build-536 banner. The exact native deactivation writer
and whether the fix removes all visible flashing remain hardware questions.
The simultaneously reported options-menu failure is independently reproduced
in the logs and is addressed in OPTIONS-536.md.

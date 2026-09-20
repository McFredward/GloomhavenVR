# Short-rest report verification — build 536 round

## Capture provenance

The new files were supplied on 2026-09-20, modification time 17:30:44 local time.
LogOutput.log has 3,604 lines; Player.log has 8,743. Both startup banners identify
ModBuild 534 / assembly 1.0.5.0, the released 1.0.5. The current dev starting point
is 8510c9d2, ModBuild 535 / version 1.0.6. The supplied capture therefore does not
contain the last short-rest correction. Peer logs remain historical build 500;
no new screenshot accompanies this report. The maintainer subsequently supplied a separate build-535 test in second_logs;
its evidence is recorded below.

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

## Follow-up hardware confirmation

The maintainer then tested the current development build and reported that the
short-rest problem appears resolved. second_logs/LogOutput.log (3,113 lines) and
second_logs/Player.log (8,077 lines), captured 2026-09-20 17:36:40, both identify
ModBuild 535 / assembly 1.0.6.0. This is the first supplied capture of the correction.

For ShieldBash, fx -59630 / plate -61088:

- 2031–2032: one inactive original starts and advances to raw 0.006.
- 2050: the original and drawn material preserve grey/flow 1 and dissolve 0.646.
- 2060: at 0.676 seconds, the duplicate LostMode request is held.
- 2066–2068: the same renderer unbind/rebind edge recurs at 0.707–0.714 seconds,
  but grey/flow remain 1 and dissolve remains 0.646 throughout, including the
  rebound material. Raw native progress independently advances 0.350 -> 0.365.
- 2071–2072: the sole native iterator completes at 2.010 seconds, raw 1; only
  then does the card leave its artwork hold and fly to the Burnt pile.
- 2085: presentation ends at 2.021 seconds with continuityChanges=0.

The formerly failing edge is present and the spent appearance now survives it.
The new log supports the maintainer's observed resolution in this local test.
A renderer binding trace alone does not prove every headset pixel, but here it
agrees with the direct hardware observation. Peer logs remain historical; there
is no new independent remote hardware confirmation.

## Decision

Retain the build-535 correction without further burn changes. The reported local
short-rest regression is hardware-confirmed resolved in this test. The exact
native hierarchy writer need not be changed: its transition now preserves the
running presentation correctly. The simultaneously reported options-menu failure
is independently reproduced in the first capture and addressed in OPTIONS-536.md.

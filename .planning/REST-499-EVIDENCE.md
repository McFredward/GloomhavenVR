# Build 499 investigation: remote long-rest flash

## Scope and provenance

Read-only evidence review against integration base `317fc045` (ModBuild 498).
No runtime change or headset verification is part of this lane.

The current main-checkout `.planning/debug/LogOutput.log` identifies ModBuild 498
at line 18 and release `1.0.0`, commit `b691d4935`, at line 71. Its `Player.log`
has the matching banner at lines 43 and 155. The supplied `remote/LogOutput.log`
is **older evidence**, ModBuild 491 at line 17, commit `050e88018` at line 70.
It must not be treated as the other viewpoint of the 498 session. No current
incident frame or video accompanies this capture; older screenshots do not
establish the rendered output at the long-rest event.

## Main finding

The 498 log records the full fallback desktop screen being created and shown
briefly **during the other player's long-rest burn**, while the remote board's
root remains active. This is a substantially stronger candidate than a speculative
incompatible card shader: it is an actual screen transition at the relevant
action, and the same transition is present in the original 491 evidence.

The evidence establishes that the screen appeared. Without a captured headset
frame, it does not prove which pixels the user perceived as the board flash or
exclude a simultaneous rendering defect.

## Build 498 event sequence

Line numbers below refer to `.planning/debug/Player.log`, unless stated otherwise.

| Event | Evidence |
| --- | --- |
| Peer long-rest action arrives | 129811: GameAction 106, LongRest at LongRest, player 2 |
| Processing raises modal mode | 129822: processing starts; 129827: `HalfSelection -> ModalUI` |
| Game resolves the rest | 129838: `PlayerLongRested`; 129851–129874: the ordinary logged call stacks move GrabandGo from discard to lost and WardingStrength from discard to hand through `CardsHandUI.HandleLongRest` / `ProxyLongRest` |
| Fallback screen appears | 129879–129885: base RT, screen placement, split UI/background routing, stereo screen activation and `FlatScreen shown` |
| Actual placement | 129880/129884: screen position `(-5.63, 10.72, 15.92)`, size `15.74 x 8.85` world units, layer 27; headset position `(-13.89, 10.72, 7.01)` |
| Camera capture and measured work | 129899–129903: ScenarioCamera and UI Camera redirected to RTs, stereo mirror created; 129907: frame 52372 takes 38.87 ms, including 11.77 ms in FlatScreen |
| Remote burn is detected | 129908 / LogOutput 21507: GrabandGo, peer 2, frame 52372, recess 1, real front |
| Screen persists across frames | 130035 / LogOutput 21534: frame 52402 takes 26.52 ms, including 11.35 ms in FlatScreen |
| Modal mode clears and screen disappears | 130037: `ModalUI -> TableIdle`; 130057–130061: camera stack released, stereo mirrors destroyed, split routing released, `FlatScreen hidden` |
| Burn artwork completes before flight | 130265–130267 / LogOutput 21560–21562: flight to burnt pile; hold 1.97 s, owner release event, 142 seated frames, zero stationary fallback time |

In the shorter `LogOutput.log`, the corresponding screen interval is
21481–21541, with `FlatScreen shown` at 21487 and the burn at 21507. The screen
starts before the mirror discovers the new lost card and closes before its flight.
There is only one `PlayerLongRested` message in this session. The other two remote
burns are OverwhelmingAssault (LogOutput 17950/17998) and Trample (18979/19016);
neither coincides with a fallback-screen show. Counting the many explanatory
mentions of long rest in diagnostics would incorrectly multiply these events.

## What this rules out or narrows

- The board's visibility instrument reports only creation at LogOutput 7289
  (frame 12804, time 170.142 s, root -619904) and lifecycle destruction at 22855
  (frame 56230, time 733.967 s). There is **no instrumented active-state change,
  teardown or rebuild during any burn**. This narrows the earlier missing-root
  hypothesis; it is not a per-eye renderer or ancestor-visibility measurement.
- `RemoteBoards = Always` is applied at 3549; the scenario gate opens at 6858
  and has no closing transition during the rest. The board is built once at 7318.
- Peer-board transparency last releases at 19579, before the long-rest window;
  there is no see-through decision or material-delivery transition during it.
- A targeted Player.log scan of 127000–130499 finds no D3D/device-loss error or
  Unity exception attached to the burn. The Hydra DNS failure at 128060–128064
  precedes it and has no demonstrated link to this screen transition.
- Missing `_AnimNoise_Mask` warnings on `Default UI Material` occur twice at
  130040–130041 after the mode returns to TableIdle. They warrant separate
  material investigation if a card defect remains; they are not evidence that
  a shader hid the board. The preceding card-move stacks are ordinary game
  diagnostic stacks, not thrown exceptions.
- The 10-second face census and board-content dump cannot prove the absence of
  a transient artifact. Conversely, their long explanatory text mentions many
  historical defects without reporting new occurrences of those defects.

## Historical cross-check

The original [492 board review](MP-492-BOARD.md) correctly left disappearance
unproven, but did not identify the fallback-screen transition. In the retained
491 remote logs, the observer of Testo's GnawingHorde long rest records:

- `remote/LogOutput.log` 30416: `FlatScreen shown`; 30427–30431: camera capture;
  30432: frame 76310, 36.01 ms, FlatScreen 12.15 ms.
- 30447: peer 1 GnawingHorde burn; 30459: `FlatScreen hidden`; 30506–30507: burn
  flight. Matching `remote/Player.log` screen lines are 269048 and 269212.

This is a separate session and reverse viewpoint, not a synchronized match to
498. It establishes that the fallback-screen mechanism already existed by 491;
the 493 performance work and 497 board-pose work cannot have introduced its
first occurrence. The overwritten 491 host log is represented only by the
earlier report and cannot be newly re-scanned here.

## Source and history lead for the implementation lane

At the reviewed base, `VRModeStateMachine.OnUiLock` maps the native UI lock to
ModalUI, and `FlatScreen.WantVisible` falls back to the desktop composite in that
mode unless another owner or `HandSuppression.BurnActive` suppresses it. That
guard's production registration sites are in `BurnCardFx`; no matching
`BeginBurn` registration exists in `RemoteBurnFx`. The event sequence makes a
foreign long-rest lock escaping the existing burn guard the leading source
path to verify and repair. Arming a guard only after the remote lost-pile poll
would be late for the first observed screen frames.

This is not a new desired animation. `HandSuppressionPatches.cs` already records
the exact historical unwanted desktop-quad flash and a trailing-modal latch.
Relevant history: `bc309119` (suppress flat burn leak), `cd42d15b` (burn tail hold),
and `252b45d6` (eliminate end-of-burn FlatScreen flash via trailing-ModalUI latch).
The existing exemption for an explicitly requested screen or a genuine
interactive fallback must remain available; the fix should cover the empty
burn-related fallback without disabling original card artwork or gameplay locks.

Validation for this report: current 498 and retained 491 log banners checked,
action/screen/burn events cross-referenced between each session's two log formats,
production guard and registration sites inspected, `git diff --check` passed.
No GPU capture, exact visual disappearance duration, owner-side 498 comparison or
post-fix hardware result is claimed.

# VR Options repeatability — build 536

## Evidence

The supplied September 20 capture is local **ModBuild 534 / assembly 1.0.5**, not 535.
`LogOutput.log:2954` names the exact fourth-opening refusal:
`CATCH-ALL FUSE: window 'GloomhavenVR.OptionsTabWindow' re-floated 4× in 60s`.
Later clicks still rebuild the curated pane (`2951`, `2964`, `3080` onward), but the
catch-all permanently excludes its presentation. This proves a presentation-admission
failure, rather than a missing open callback or a three-attempt native settings limit.
No matching menu-entry exception was logged. Remote logs remain historical build 500.

## Changes

- The repeat limiter now recognizes the already registered mod-owned settings window,
  including its existing exact-name fallback. It bypasses both stale name suppression
  and the repetition counter. Unknown HUD banners retain their three-per-minute fuse;
  normal float eligibility, close routing and native game flow are unchanged.
- While their actual host menu is visible, only the mod's cloned rows regain their own
  active/enabled/interactable state and native focused appearance. Other menu rows and
  host windows are untouched. This is defensive repair for the reported disappearing or
  grey entry; the supplied logs do not establish which writer changed that row's appearance.
- A transient entry/seat exception no longer disables menu maintenance for the session.
  Recovery retries after two seconds, with one normal failure report per pass per module.
- Failed pane injection cleans its partial clone before retry. The first two failures
  back off two seconds, later failures thirty seconds; a new native host retries immediately.
  Only the first three consecutive failures log; a successful build resets the streak.
  This replaces a separate source-proven permanent failure latch, not the recorded fuse cause.
- Main-menu discovery waits for pane recovery without consuming its scan budget; already known
  hosts restore a lost row without another scene scan. Failed pause-row creation retries on the
  same host with a cooldown and a single failure report.
- Replaced native hosts tear down the detached persistent clone. Delayed hidden callbacks
  from an older clone or an already reopened pane cannot deselect the current opening.

No per-frame hierarchy scans or component-array allocations were added. Settings remain
local menus; no gameplay data, network protocol, native pause gating or shared UI changed.
An incompatible/missing donor prefab cannot be promised to construct successfully; failures
remain visible in bounded logs and keep retrying instead of permanently losing access.

## Validation

`bash scripts/vr-options-tests.sh` executes extracted production admission, family classification,
entry/seat maintenance, injection retry and hidden-callback methods against narrow native stubs.
It covers **5,536 runtime assertions**, four source bindings and **15 mutation controls**.
Four simulated scene contexts each run 128 opening/closing cycles, including stale suppression,
row availability, untouched native gates, transient exceptions, retry cadence, bounded logs,
partial-clone cleanup, stale-close callbacks, delayed pane availability and repeated row recreation. These fixtures are not headset evidence.

The new harness is registered in the local wire-test umbrella and full dev/PR CI.
Integrated validation passes: the complete source/runtime guard, 254,565 real-runtime
wire assertions, strict Release with zero errors/warnings, bilingual documentation,
Actionlint and whitespace. Config/patch/log-token surfaces remain 625 / 174 / 4,742;
patch inventory remains 132 classes / 200 methods. Guard exit 1 only reflects the
expected compiled difference from baseline 080c505e9: 101 changed, 78 added/removed
and one order-only move. A separate comparison against the saved build-535 compiled
output finds only ModalFallback, VRMenuEntry and VROptionsTab behavior changes;
all other differing types contain only 535 -> 536 build-number substitutions.

Hardware verification remains: repeatedly open/close VR Options in scenario, Guildmaster, tutorial and main menu; also
switch between pause and VR Options quickly and confirm the entry remains visible and clickable.

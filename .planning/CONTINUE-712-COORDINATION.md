# Visible Continue follows native pointer permission — Build712

Root reserves712 on dev a6b862269 for the October19 chest-loot deadlock.
Fresh local LogOutput/Player logs identify710/a3b7ed777, not711. Remote Player.log
is still October11 vanilla evidence; no paired current remote test is supplied.
No new screenshot/video was supplied. Hardware copies and hashes are frozen at
`.planning/debug/continue712/hardware/` before analysis.

Hardware shows the chest activated during EndTurnLoot and the native reward
showcase started with an item. RewardShowcase logs canContinue=False; pointer
entry says interactable=False. Forty-eight uGUI RewardContinue click deliveries
never produce the native forwarded-input line. The same ESC window remains
floated throughout this reward and its module-shutdown ownership report explicitly
says the native window is OPEN. Native UIRewardsManager.ProcessRewards has no ESC
predicate for MouseClickLeft/isConfirmPressed. Its separate gamepad key handler
has an ESC blocker. The VR pointer adapter incorrectly imported that blocker into
its visible button, and AnnouncementContinue repeated the extra prerequisite.

Restore exact pointer semantics; keep native live-window, reveal/readiness,
input-latch, actor/network authority and original callback gates. No ESC close,
automatic Continue, hidden-window skip or direct gameplay write is authorized by
this fix. UIRewardsManager uses a MEC coroutine rather than Behaviour.Update;
Behaviour.enabled is also not its native mouse readiness predicate. That second
source-proven mismatch is covered without claiming it was the hardware operand.
The native pending input flag is not reset by the mod. First ProcessReward clears
it; later completion/start behavior is covered through exact native methods.

Initialized workers from currentdev a6b862269:

- continue712_fix: production RewardShowcase.cs and its button classes only.
- continue712_audit: initial read-only audit of adjacent continuation adapters;
  approved bounded AnnouncementContinue.cs, announcement fixture and script.
- continue712_tests: existing RewardShowcase test directory and script only.
- Root: logs, independent review, version/STATE, focused integration gates,
  Debug delivery, direct dev push and evidence-preserving cleanup.

Audit other adapters against original callbacks. Postquest introductions/native
button availability, UIMessage queue continuation, DialogPopup choices and the
host-routed enemy-information confirmation keep their genuine rules. An absent
new source defect does not justify rewriting them. Other project-context primary
confirmed712 and Reward/Dialog ownership are free; its native passthrough work
continues independently, with no Game-Repo overlap.

Focused runtime/source evidence is appropriate for this bounded repair. Reuse
unchanged native/wire/figure711 evidence honestly instead of rerunning the entire
local catalog. Every completed/failed run must use cleanup_completed_run after
its processes are reaped, with an explicit evidence line; preserve source/results,
hardware and current deliverables. Remove only these completed owned worktrees
after byte-verifying archived evidence and integrated source hashes.

## Integrated result

Worker source commits634c6a008/87d9ea913 (RewardShowcase),85317fae72
(Announcement) and4300542a0 (native reward fixture) were independently reviewed
and cherry-picked into dev. Only RewardShowcase and AnnouncementContinue change
runtime behavior. Root stamps712, documents the standing pointer rule in AGENTS
and keeps the existing suite registrations (229 local/102 CI).

Passing evidence under `.planning/debug/continue712/`:

- `integrated-input-hashes.json` verifies all33 relevant source/script/fixture/
  native inputs against the full worker executions. Identical causal controls
  are inherited rather than rebuilt merely for root bookkeeping.
- `workers/tests-evidence.tar.gz`: RewardShowcase3275 assertions and27 compiling
  causal controls. Source-exact native StartRewardsShowcase, ProcessRewards and
  EndProcess are compared with the actual read-only game source. The120-case
  matrix uses native mouse coroutine progress as its permission oracle, covering
  offline, owning/observing host/client, local-only processing, gamepad/mouse,
  manager enablement, contexts and repeated/grouped reward lifecycles.
- The actual production RewardShowcaseButton.Tick/Continue and RewardContinueButton
  source execute. Selectable/pointer primitives, button geometry, artwork, MEC
  scheduling and transport delivery are explicit portable boundaries. No real
  Unity raycast, rendered pixels, headset or live Photon proof is claimed.
- `workers/audit-evidence.tar.gz`: adjacent source audit, actual native references,
  announcement349 assertions/seven compiling controls. The old extra ESC veto
  fails on the ready pointer assertion. Genuine readiness, hidden-window,
  ownership, animation and duplicate-input gates remain covered with ESC open.
- Both worker lanes retain failed development builds/source and cleanup receipts.
  A native nullable boundary and the former synthetic already-running constructor
  assumption were corrected in the fixture; neither is reported as a green run.
- `source/` passes the complete source group16/16, including all locked orders;
  it is not a complete local gameplay-gate run. Strict Debug/Release have0warnings
  and0errors; `deliverables/` retains original build metadata/diff and hashes.
- `compiled-scope/verdict.json` reuses the retained strict711 private baseline:
  exactly two owned behavior types, eight inlined version consumers and three
  metadata files change. No type added/removed; no shared baseline overwritten.
  A later NetProtocol comment-only clarification changes no compiled behavior.
- `surface-{before,after}.json`:669config keys/241Harmony entries unchanged;
  all4806old log tokens remain, REWARD SHOWCASE BLOCKED is the only addition.
  Real disabled reasons are change-gated normal Note; pending input stays Debug.
- `docs-i18n.log` passes. Unchanged figure711, physical pointer/geometry and actual
  wire300598 evidence are inherited; no new full local/wire run is claimed.

Own build cache cleanup removed61945969bytes; worker fix30507005, announcement
218049, and reward final1091540 plus retained-failure cache receipts were cleaned
through the canonical API after reaping. All three owned worktrees were removed
only after byte-verifying their complete debug archives and clean tracked state;
worker branches remain local. Unrelated worktrees and shared references remain.

The user's follow-up question was answered from source and hardware: Escape Menu
means the native Gloomhaven game menu, converted into the movable VR window named
UI Scenario Esc Menu. It is distinct from system/runtime dashboards. Its being
open must not add a pointer prohibition absent from the original Continue callback.

Final headset and live multiplayer acceptance remain to be checked in hardware.

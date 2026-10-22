# Item surrender result cleanup — Build720

## Supplied evidence and cause

The main `LogOutput.log` and `Player.log` identify Build719. The remote files
still identify Build717; they are not paired evidence for this test. Exact input
hashes, banner lines and bounded event extracts are retained in
`.planning/debug/item720/hardware-extract.json`.

Main `LogOutput.log:5851` selects Major Healing Potion through the native item
picker, then closes the item fan while retaining that original in the berth.
Line5925 reports native confirmation accepted. Line5928 immediately retires the
same card as an unconfirmed placement because CurrentHand has become null;
line5929 ends the picker. That cancellation message incorrectly says nothing
was used, although the preceding native confirm succeeded.

The accepted result sets `_finishingUseChip` and installs a native FX completion
callback. The closed fan still retains the same chip as `_keptClip`. The off-turn
PileViewer path unconditionally retired that survivor. ItemChip.Update gives
collapse precedence over result FX, so that retirement bypassed the callback
that clears the finishing owner and hides the berth. While collapse ran, the
regular item tick could reassert the overlay; after destruction there was no
remaining closed-fan decision to hide it. Character switching happened to clear
this leftover presentation.

## Repair

`RetirePlacedCardIfAny` now preserves a confirmed result: its original chip
services native burn/tap playback independently of the action hand. EndDemand
keeps that chip’s berth parent active until completion. Completion clears the
source registration, survivor/index and pending flag and hides the berth, or
retains the ask if a following native demand is already active. Pile destruction
clears its finishing reference too. Unconfirmed placements still cancel through
the existing native deselection/return routes.

ConfirmDemandPick captures the original demand kind before invoking the native
callback, which can synchronously close or replace the picker. Refresh and
reward-forfeit presentation therefore cannot inherit a successor’s demand kind.
The native picker remains the only gameplay confirmation/authority seam.

Remote visibility continues to use the original record13 board-UI bit and
record26 clipped index: NetAvatarDriver reads PlayTray.ItemUseSlotShown and
ItemsPile.RecessOwner.ClippedChipIndex. Completion releases both through their
existing producers. No protocol, default, localization or asset change; wire3,
next-free123.

## Validation

Strict Release0warnings/errors, all16 source suites and all306431 unfiltered
golden assertions pass against the repaired production source. Item appearance
passes645 assertions/6 causal controls, active-dismiss API145/6 and its physical
flow83/12. The first item-burn run exposed a fixture boundary missing the newly
read demand field; no burn assertion ran in that failed invocation. Its corrected
full integrated rerun passes227 assertions/all11 causal controls; expanded
item-placement passes608 assertions/all12 causal controls. Five distinct affected
suites now have passing integrated coverage. No production changed after the
strict/source/full-golden checks; the later integration is fixture-only.

The worker reproduced the actual off-turn preemption with the Build719 method
bodies and retained that failing source/assertion evidence. Expanded placement
coverage executes actual Tick/TickDemandPick, ConfirmDemandPick, retirement,
result begin/completion and clipped-index/berth-owner kernels. Native callbacks,
Unity lifetime and effect playback remain explicit boundaries in that fixture;
actual extracted PlayUseThenCollapse/TickUseFx is exercised separately by the
item-burn suite. No simulated test is described as a live native/headset run.

Private compiled comparison against actual719 finds one intended behavior type
(ItemsPile) plus nine types differing exactly by the inlined719→720 build number;
there are no added/removed types. Source surface retains all669 settings,
242 patches and4816 log markers, and every bundle check passes. The private
comparison script’s nonzero summary status reflects these intentional type
changes, with no failed build/source/surface/bundle gate. Complete diff, hashes,
strict build and test receipts remain under `.planning/debug/item720/` and the
separate focused/source/final-golden directories. EN/DE document checks pass.

This is a bounded lifecycle repair. It reuses unchanged Build719 coverage and is
not a new full local-suite sweep. Actual headset pictures and native multiplayer
playback of Build720 remain unverified. Completed test caches are cleaned while keeping
logs, source snapshots, hashes, results and current deliverables. The completed
owned worker worktree was removed only after197 evidence files were archived
and byte-compared into `.planning/debug/item720/worker-evidence/`; the worker
positive proof used byte-identical integrated production. Other worktrees and
hardware inputs were preserved.

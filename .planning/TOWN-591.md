# Build 591 — town entry and map selection

## Build 590 hardware evidence

The new local `debug/LogOutput.log` identifies ModBuild 590. No matching new peer
trace was supplied for this round. The enchantress-entry frames 3606 and 12989
take 176.37 and 138.52 ms in total. `TownServicePresentation.Visit` accounts
for 147.56 and 109.60 ms respectively. The old Visit scope includes both the
game's native `UINewEnhancementWindow.EnterShop` and the mod's subsequent
original-widget folio conversion, so it cannot identify which stage dominates.
Decompilation shows `EnterShop` synchronously clears the previous options,
enables enhancement mode and repopulates the owned-character card slots. The
mod also converts the original 549-transform enhancement inventory at entry.

The map selection log records 12 edges from a selected character to nobody
while changing town destination; ten fall inside the previous 4 Hz selection
floor's polling window. The native call path is `DisableMapOptions` or
`EnableMapOptions` → `CloseWindows`/`DeselectCurrent` → selected portrait
`Escape`/`OnClick` or `OnCharacterSelect(false)`. The floor reselects the same
owned character later, but a visible frame or callback can observe the gap.

## Changes

- `MapSelectionTransition` retains the already selected locally controlled
  portrait only inside the synchronous native map-options change. Native
  subwindow/tab cleanup, user-initiated clicks, the first map tutorial and flat
  maps keep their original behavior. A finalizer clears the scope on errors.
- The first `CanvasConversion.ApplyModLayer` sweep records each transform's
  original layer without searching the growing restoration list. A newly
  converted panel begins with an empty list and the traversal sees each node
  once. Later pooled-child sweeps retain the duplicate guard and the first
  original layer for correct rollback. An extracted production 549-node fixture
  measured 0.358 to 0.068 ms per initial sweep over 150 runs. This is a real
  reduction, **not** an explanation for the whole 110–148 ms Visit spike.
- Bounded performance scopes now separate `TownEnhancement.NativeOpen`,
  `TownEnhancement.FolioOpen`, `TownEnhancement.NativeOptions` and
  `TownEnhancement.NativeListMask`. The next Debug hardware trace can place the
  remaining time without altering the native enhancement transaction.
- The hidden, default-off Cheats page has an immediate +100 gold action for
  the currently selected owned map character. It uses native `ModifyGold(100,
  false)`, the native gold-changed message and native save. If the game uses
  shared party gold, it credits that purse instead. It refuses online sessions,
  missing map/selection and an unavailable save. Each press is repeatable.

## Verification and remaining test

Focused source-linked tests cover the native map-options guard, normal clicks,
first-map tutorial and exception cleanup, plus a rollback negative control for
the 549-node conversion. On the integrated tree, 14/14 source gates passed.
The complete runtime run recorded 79 passes and one fixture compile failure:
the stand-alone enhancement handoff harness lacked a placeholder for the new
`PerfMonitor.Scope` call. After adding that placeholder, the affected suite
passed its production case and 61 negative variants. Thus all 80 runtime
suites have passing final-tree evidence without repeating the other 79. The
wire binary passed 286,609 assertions; bundle format and surface checks passed;
strict Release compilation succeeded with zero warnings/errors, as did the
EN/DE docs and whitespace checks. The one-shot `refactor-guard` command itself
exited before its final compiled-form comparison because of that earlier
fixture failure; it must not be reported as a single green invocation.

The headset must confirm that the portrait stays visually stable at all three
residents, the gold action changes the selected character's purchasable balance,
and the enchantress still accepts and upgrades a card. In the Debug log, compare
the new `TownEnhancement.*` scopes during a slow first and repeated visit. The
large entry hitch is **still open** until that split establishes its dominant
stage and the resulting change is measured in hardware. Do not infer that a
0.29 ms managed-fixture saving eliminated a 100 ms headset stall.

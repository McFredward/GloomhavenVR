#!/usr/bin/env python3
"""Every mod patch on a type the game dispatches NETWORK ACTIONS into must be classified.

WHY THIS EXISTS
---------------
`ActionProcessor.TryProcessNextAction` wraps the ENTIRE game-action dispatch in

    catch (Exception ex) { FFSNetwork.HandleDesync(ex); }

and `GHNetworkControllable`'s eight Bolt state callbacks do the same. So an exception
thrown anywhere under that dispatch — INCLUDING out of one of our Harmony patch bodies —
is not logged as a mod bug. It is shown to the player as the GAME's "Desynchronization
occurred" dialog, and the session is shut down with a single Main Menu button.

Twelve of the mod's patch classes sit on such a type today, and the mod patches the five
heaviest receivers in the table (Choreographer 27 actions, CardsHandManager 13,
NewPartyDisplayUI 10, UIReadyToggle 8, TakeDamagePanel 3). That is not a reason to panic
— most of those bodies genuinely cannot throw — but it IS a reason never to add a
thirteenth without somebody looking.

So this script does not guess whether a body is safe. It asserts that every patch class
on a receiver type appears in `docs/NET-ACTION-SURFACE.md` with a verdict, and fails on a
patch that has never been classified. The verdict is a human judgement recorded once;
the check is that no new one slips in unjudged.

    check-desync-surface.py            verify the ledger against the source
    check-desync-surface.py generate   rewrite the ledger's table from source

PROVENANCE OF THE RECEIVER LIST
-------------------------------
Derived from `decompiled/GH.Runtime/FFSNet/GameAction.cs` — the static dispatch table of
~121 `GameActionType -> delegate` entries — plus the receivers of
`GHNetworkControllable`'s eight `On…Changed` Bolt state callbacks. `decompiled/` is a
read-only reference tree OUTSIDE this repository, so the list is committed here rather
than regenerated. It changes only when the game does. Full analysis with line citations:
`.planning/multiplayer/DESYNC-ANALYSIS.md`.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INVENTORY = ROOT / "docs" / "PATCH-INVENTORY.md"
LEDGER = ROOT / "docs" / "NET-ACTION-SURFACE.md"

# Types the game dispatches network actions / networked state changes into. See PROVENANCE.
RECEIVERS = {
    # --- GameAction dispatch table, by number of actions dispatched -------------------
    "Choreographer": 27,
    "CardsHandManager": 13,
    "MapChoreographer": 11,
    "NewPartyDisplayUI": 10,
    "APartyDisplayUI": 3,
    "UIReadyToggle": 8,
    "UIGuildmasterHUD": 4,
    "SaveData": 3,
    "UIDistributeRewardManager": 3,
    "TakeDamagePanel": 3,
    "SceneController": 2,
    "UIUseItemsBar": 2,
    "UIUseAugmentationsBar": 2,
    "UIUseAbilitiesBar": 2,
    "UIShopItemWindow": 2,
    "UIScenarioDistributePointsManager": 2,
    "UIMultiplayerSelectPlayerScreen": 2,
    "UIMultiplayerEscSubmenu": 2,
    "UIAbilityCardPicker": 2,
    "MultiplayerImportProgressManager": 2,
    "ItemCardRefreshPicker": 2,
    "UIRewardsManager": 1,
    "UIResetLevelUpWindow": 1,
    "UIMultiplayerLockOverlay": 1,
    "UIMapMultiplayerController": 1,
    "UIEventPanel": 1,
    "UIActiveBonusBar": 1,
    "ScenarioRewardManager": 1,
    "PingManager": 1,
    "ItemRewardLosePicker": 1,
    "UIDifficultySelector": 1,
    "UINewAdventureResultsManager": 1,
    "BattleGoalMultiplayerService": 1,
    "HouseRulesSettings": 1,
    "DebugMenu": 1,
    # --- GHNetworkControllable's eight On…Changed Bolt state callbacks ----------------
    "ScenarioRuleClient": 0,
    "UILevelUpWindow": 0,
}

VERDICTS = {"ISOLATED", "GUARDED-DEEPER", "CANNOT-THROW", "SELF-GUARDED", "WAIVED"}


def patched_classes() -> dict[str, tuple[str, set[str]]]:
    """{patch class -> (source file, {receiver types it targets})} from the generated inventory."""
    if not INVENTORY.exists():
        sys.exit(f"error: {INVENTORY} is missing — run scripts/patch-inventory.sh generate")
    out: dict[str, tuple[str, set[str]]] = {}
    cur_cls = cur_file = None
    for line in INVENTORY.read_text(encoding="utf-8").splitlines():
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        if len(cells) < 2:
            continue
        m = re.search(r"`([A-Za-z0-9_]+)`.*?<sub>([^<:]+):\d+</sub>", cells[0])
        if m:
            cur_cls, cur_file = m.group(1), m.group(2)
        tgt = re.match(r"`([A-Za-z0-9_]+)\.", cells[1])
        if not tgt or cur_cls is None:
            continue
        if tgt.group(1) in RECEIVERS:
            entry = out.setdefault(cur_cls, (cur_file or "?", set()))
            entry[1].add(tgt.group(1))
    return out


def ledger_verdicts() -> dict[str, str]:
    """{patch class -> verdict} parsed from the ledger table."""
    if not LEDGER.exists():
        return {}
    out: dict[str, str] = {}
    for line in LEDGER.read_text(encoding="utf-8").splitlines():
        m = re.match(r"\|\s*`([A-Za-z0-9_]+)`\s*\|[^|]*\|\s*\*\*([A-Z-]+)\*\*", line)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def main() -> int:
    found = patched_classes()
    recorded = ledger_verdicts()

    unjudged = sorted(set(found) - set(recorded))
    stale = sorted(set(recorded) - set(found))
    bad = sorted(c for c, v in recorded.items() if v not in VERDICTS)

    if unjudged:
        print("error: these patch classes sit on a type that receives network actions and are", file=sys.stderr)
        print("       NOT classified in docs/NET-ACTION-SURFACE.md. An exception thrown from one", file=sys.stderr)
        print("       of them is shown to the player as the GAME's desynchronisation dialog.", file=sys.stderr)
        for c in unjudged:
            f, ts = found[c]
            print(f"         {c}  ({f})  targets: {', '.join(sorted(ts))}", file=sys.stderr)
        print("       Read the body, then add a row with one of: " + ", ".join(sorted(VERDICTS)), file=sys.stderr)
        return 1

    if stale:
        print("error: docs/NET-ACTION-SURFACE.md classifies patch classes that no longer target a", file=sys.stderr)
        print("       receiver type (renamed, retargeted or deleted): " + ", ".join(stale), file=sys.stderr)
        return 1

    if bad:
        print(f"error: unknown verdict on: {', '.join(bad)}", file=sys.stderr)
        return 1

    tally: dict[str, int] = {}
    for v in recorded.values():
        tally[v] = tally.get(v, 0) + 1
    summary = ", ".join(f"{n} {v.lower()}" for v, n in sorted(tally.items()))
    print(f"net-action surface: {len(found)} patch classes on {len(RECEIVERS)} receiver types, "
          f"all classified ({summary}).")
    return 0


def generate() -> int:
    found = patched_classes()
    recorded = ledger_verdicts()
    rows = []
    for cls in sorted(found):
        f, ts = found[cls]
        v = recorded.get(cls, "TODO")
        rows.append(f"| `{cls}` | {', '.join(sorted(ts))} | **{v}** | _(fill in)_ |")
    print("\n".join(rows))
    return 0


if __name__ == "__main__":
    sys.exit(generate() if len(sys.argv) > 1 and sys.argv[1] == "generate" else main())

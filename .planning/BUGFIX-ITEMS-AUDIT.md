# Bug Fixes v5.0.0: native item UI audit

2026-10-09. Worker base `f006ecf881246ce57ef535d7dfab91c860b09eae`
(dev649). This is an independently bounded item-UI lane; root integration owns
registration, build number, credits, patch inventory and the final gate.

## Provenance and individual decisions

Upstream: [Gloomhaven Bug Fixes on Nexus](https://www.nexusmods.com/gloomhaven/mods/8)
by **fingoldfish**; [GitHub source](https://github.com/gummyboars/gloomhaven-bugfixes)
by **gummyboars**, tag `v5.0.0`, commit
`941ac18baf22d81aaab96cdc6918ce3f79fb4020`. Source MIT notice is
Copyright (c) 2025 gummyboars. Root must retain that notice alongside the requested
README credits. Supplied DLL SHA256:
`143624040714a318b71876bdf98eed482af02ac469f3071b3d4046ed96e55f7e`.
The supplied DLL bodies match the corresponding `QuestItem.cs` / `Dopple.cs`
source patches; this audit does not substitute a later upstream version.

| Upstream patch | Current native defect verified? | VR compatibility and mixed unmodded clients | Decision |
|---|---|---|---|
| `Patch_MultiplayerActionSelectionCrash` / `UIUseItemsBar.ShowUsableItems` | Yes. Native `Any(IsItemInteractable)` runs before assigning `actor`; the campaign quest-item branch reads `actor.IsUnderMyControl`. First use can null-reference. A previous non-null actor also gives the wrong ownership result. | Repairs only local native UI context to the method's existing valid inventory-owner argument. Original native predicate, ownership denial, callbacks and method continue. No replicated model, action, protocol or version changes. Existing local/observer item presentation remains native. | Adopted as `NativeBugFixes_ShowUsableItemsActor`, VR-gated, valid inventory only, guarded fallback. |
| `Patch_MultiplayerDopplegangerDesynch1` / `ProxyUseItemBonus(GameAction)` | Yes. The numeric original replays against the hidden bar's last actor; a missing actor crashes, and a stale actor dereferences a missing item. Upstream's null-only assignment does not cover the stale non-null case. | Resolve the native action's exact ActorID using native `FindPlayerActor`, and prove that actor owns the exact ItemToken NetworkID before assigning only bar context. Native replay continues with the original action/token, `networkActionIfOnline=false`, `saveState=false`. Visible native slot replay is left untouched. No mod handshake is required by an unmodded sender/recipient. | Adopted with stricter identity/valid-input validation as `NativeBugFixes_ItemReplayActor`; null and stale non-null contexts are covered. This is not a claim to fix every Doppelganger interaction. |
| `Patch_MultiplayerDopplegangerDesynch` / `ProxyUseItemBonus(uint, ItemToken)` | A hidden summon whose inventory lacks the item does reproduce the missing-item dereference. The upstream substitution avoids lookup failure when its summoner owns the item, but a correct gameplay repair is not established merely by finding that item. | This overload supplies no action ActorID. Upstream switches from summon to summoner and then the original `UseItemService(actor)` / `ScenarioRuleClient.ToggleItem(item, actor)` use the substituted inventory owner. Native infusion attribution and inventory callbacks also depend on that actor. Mixed-peer equivalence cannot be inferred. `FindPlayerActor` searches native player GameObjects, not arbitrary hero summons. | Rejected. No rule-library, inventory-sharing or summoner-getter change in this lane. A valid native action carrying the real player inventory-owner ID can safely repair an old summon UI context through the adopted GameAction patch. |

## Native causal paths

Read-only current game decompilation in the main checkout:

- `decompiled/GH.Runtime/UIUseItemsBar.cs:414`: ShowUsableItems evaluates
  `inventoryOwner.Inventory.AllItems.Any(IsItemInteractable)` first; line431's
  actor assignment comes afterwards. The campaign quest-item predicate at452
  reads that field. ShowItems at364 assigns the same owner on the other branch.
- `UIUseItemsBar.cs:560`: GameAction overload passes the token directly to the
  numeric overload and does not inspect ActorID.
- `UIUseItemsBar.cs:565`: hidden replay searches `actor.Inventory.AllItems`, sets
  native chosen elements, infuses/reserves elements and calls
  `UseItemService(actor).UseItem(... false, ..., saveState:false)`.
- `decompiled/GH.Runtime/UseItemService.cs:12`: service captures its inventory
  owner; line54 forwards the exact actor/item to ScenarioRuleClient.ToggleItem.
- `decompiled/GH.Runtime/Choreographer.cs:2235`: FindPlayerActor scans
  `m_ClientPlayers` and compares original actor IDs. The ordinary player
  creation paths populate this list at1318/1330/1342.
- `decompiled/GH.Runtime/FFSNet/GameAction.cs:407`: UseItem and
  ClickItemBonusSlot both dispatch to this original native bar replay.
- `decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/ScenarioRuleClient.cs:620`:
  EToggleItem invokes the supplied actor's own inventory. Native callback/model
  behavior is therefore not interchangeable merely because an item was found.
- `decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/CHeroSummonActor.cs:116`:
  Summoner returns the native player cache/resolution; it does not identify the
  actor of a numeric replay message.

## Mixed-client behavior

For an **unmodded sender / VR receiver**, the repaired UI resolves the already
received ordinary UseItem/ClickItemBonusSlot action against its existing actor
and item, then executes the original native replay. It emits no replacement
action, consumes no action, fabricates no item and does not rewrite a token.

For a **VR sender / unmodded receiver**, outgoing ordinary item actions and token
serialization stay native. The native UI ownership predicate now consults its
actual method argument instead of a stale UI field, so a valid current actor's
quest item is available to that actor. No peer needs BugFixes.dll or GloomhavenVR
to parse or execute those unchanged actions. These local repairs cannot patch
an unmodded recipient's own existing null/stale-item-bar defect; that limitation
is separate from introducing a mixed-version protocol/state incompatibility.

The source changes no `GameState`, phase, inventory membership, item definition,
action/token/version, rule-library method, replay dispatch, or native slot
OnPointerDown. Invalid inputs/failed actor resolution preserve native handling
and its existing error rather than swallowing it. Both prefixes catch their
own lookup failures; diagnostic output is capped to one warning and cannot
escape into the game's desynchronization handler.

## Validation and its limits

`python3 scripts/native-bugfix-items-runtime/run.py` compiles the exact production
file into actual Unity2021.3.5f1 fixtures with original current GH.Runtime,
ScenarioRuleLibrary and Photon/Bolt assemblies. The harness executes original
ShowUsableItems, its ownership predicate, native inventories/actor IDs,
FindPlayerActor, and **both original ProxyUseItemBonus bodies**. It first
reproduces the null/stale native bugs with patches absent, then checks the
repairs, visible-slot behavior, invalid-input/native-failure behavior, VR-off
behavior, unpatch, bounded anomaly logging and native network version.

The rich-token case additionally checks unchanged nonempty chosen/infused
elements and the exact actor forwarded to native downstream calls. Summon
negative cases prove that no unauthenticated owner substitution ships.

External seams are explicitly bounded: VR state/logger; Bolt IsOnline without a
live server; downstream ShowItems widget creation; downstream UseItemService
full-scenario UI/phase processing; visible slot downstream click; element
reservation UI/authoritative infusion entry and its UI update. These boundaries
capture original calls and arguments; they are not evidence of a complete live
scenario or all gameplay effects. This is not a two-headset multiplayer pass.

Final worker scope: production58 native-runtime assertions and six causal
source controls (disabled show repair; weakened null-only show; disabled replay;
upstream null-only replay; ignored VR gate; ignored item identity). The final
seven-variant receipt is `.planning/debug/native-bugfix-items-runtime/run-1gy_7lci/`
(57 assertions before the final explicit logger-type assertion). Its production source SHA256 is
`434bceab36c96eb50b3e6b05446e7dbad1be69bf7d34d7ff0914f65c7ca314cd`.
The final production-only logger/type repeat passes58 assertions at
`.planning/debug/native-bugfix-items-runtime/run-dnzzwi2w/`; the unchanged six
source controls inherit the previous seven-variant pass. A bounded repeat is
not relabeled as another complete seven-variant run.
Root records the integrated receipt after registration. The whole production
Debug and Release builds pass strictly with0 warnings/errors. Docs i18n passes
all5 bilingual player-document pairs. The source-surface snapshot reports
663 config keys /233 Harmony source entries /4790 log tokens. Two native patch
classes and one bounded warning are added; source entries are not a count of
classes. Registration and its final surface diff
remain root work. No full-catalog claim is made by this worker lane.

The logger boundary deliberately throws during the first failed native actor
resolution. The guarded native replay still completes and only one warning is
attempted, proving that a logger failure cannot escape from the repair.

Failed exploratory fixture receipts are retained, not counted as passes:
initial missing bolt reference; one constant false source mutation producing an
unreachable-code compiler error; one nonempty-element fixture that incorrectly
set a generic singleton while InfusionBoardUI has its own static Instance. The
instrument was corrected against the real native class before repeating that
scope. Earlier53-assertion production/six-control receipts remain evidence for
their narrower scope, rather than replacing the failed rich-token attempt.

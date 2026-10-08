# Build651: individual Bug Fixes 5.0.0 review

The maintainer requests every supplied fix to be verified against the current
game, checked for VR compatibility, and incorporated only when both hold. The
follow-up explicitly requires play with unmodified multiplayer clients. This
review therefore treats mixed-client compatibility as a separate mandatory gate;
leaving the native network version unchanged does not make a rule change safe.

## Inputs and attribution

- Supplied archive: `.planning/debug/BugFixes.dll-8-5-0-0-1768874634.zip`, SHA256
  `38c79e03cb951dc8005222c1489f16141995d4b7e4db752e1e20321c9e8e360c`.
- Extracted DLL: SHA256
  `143624040714a318b71876bdf98eed482af02ac469f3071b3d4046ed96e55f7e`.
- [Bug Fixes by fingoldfish](https://www.nexusmods.com/gloomhaven/mods/8?tab=description),
  with source by [gummyboars](https://github.com/gummyboars/gloomhaven-bugfixes/tree/v5.0.0).
  Release `v5.0.0`, commit `941ac18baf22d81aaab96cdc6918ce3f79fb4020`.
- Both the supplied DLL and that release source were inspected. The DLL contains
  eleven Harmony patch classes plus its bootstrap/network-version assignment and
  an unused diagnostic helper. It was decompiled as data, never loaded as a plugin.
- Current original game inputs, source/DLL snapshots and their hashes are retained
  under main-checkout `.planning/debug/bugfixes-audit/provenance.json`.
  In particular `GH.Runtime.dll` is
  `fcfcd2e420299139b6cba9a5fedb52c9cae60bb5e1292adb84152c14d5e8f495`.
- Adapted UI repairs retain upstream's MIT copyright in
  `packaging/licenses/Gloomhaven-Bug-Fixes-MIT.txt`. Both packaging paths copy this
  notice; README credits include author and Nexus URL. The foreign DLL is not shipped.

## Every supplied patch

| Upstream patch | Current native defect / purpose | VR and unmodified-client assessment | Decision |
| --- | --- | --- | --- |
| `Patch_ShowVersion` / `MF.SetVersion` | Appends foreign-mod branding; no game defect. | No repaired behavior. Would misidentify the integrated VR plugin. | Exclude. |
| `Patch_RestorePhase` / `CardsActionControlller.RestorePhase` | Restores cached card references without restoring the controller's extra-turn restriction. A stale restriction can disable the remaining legal half. | A narrow repair of this UI field can retain original played-side, ownership and bonus restrictions. Upstream's broad `SetInteractable(true)` reset is redundant during selection and unsafe during target phases. | Adapt only matching-owner cached card-choice UI context. Native `SetPhase` remains responsible for every button. No gameplay-stack patch. |
| `Patch_StartActorExtraTurnImmediately` | Trims duplicated `CurrentAction` entries in the shared native extra-turn stack. | Changes deterministic turn/rule state, unlike local card UI. Unmodified peers retain their original stack and actor-action interpretation. Also falls inside the project's protected rule-library surface. | Exclude. |
| `Patch_DivineIntervention` | Gates the native damage redirect/behavior trigger on the bonus toggle. | Redirected damage and bonus consumption are shared rules. Applying only on VR participants would produce a different model for the same input. | Exclude. |
| `Patch_MultiplayerActionSelectionCrash` / `ShowUsableItems` | The native campaign quest-item ownership predicate reads cached `actor` before it is assigned to `inventoryOwner`. Null first use can throw; a stale actor gives the wrong ownership result. | Assign exactly the original incoming owner before the original predicate. Native item list/filter/use callbacks and ownership remain unchanged. | Adapt as `NativeBugFixes_ShowUsableItemsActor`. |
| `Patch_MultiplayerDopplegangerDesynch1` / `ProxyUseItemBonus(GameAction)` | Hidden native replay trusts its cached actor rather than the received action's actor ID. Null/stale UI context can fail replay or select the wrong inventory. | Resolve only the exact native player identified by the original action, with the exact token item in that player's inventory. Original replay runs with the original token and network/save flags; shown native slots are untouched. | Adapt as `NativeBugFixes_ItemReplayActor`, including stale non-null context. This is not a claim to fix every Doppelganger rule defect. |
| `Patch_MultiplayerDopplegangerDesynch` / numeric replay overload | Falls back from a summon to its summoner when the cached inventory lacks the token item. | Numeric overload has no action-owner ID; changing the caster/inventory can change `ToggleItem` rules. Summon/summoner inventories differ. No equivalent mixed-peer transaction is established. | Exclude the substitution. A valid original action identifying its actual player is covered by the separate UI repair. |
| `Patch_CheckNonTrophyAchievements` | Verified current Guildmaster C-C-C-Combo progress is lost when result UI clears transient scenario stats before the non-trophy check. | Upstream writes serialized counters and can roll rewards/send notifications on each participant without host/idempotence guards. Native proof yields different persisted counters for original and modified ordering; duplicate evaluation counts twice. | Exclude despite the real defect. Do not claim crossplay safety from a UI hook. |
| `ReplaceSummonerGetter` / `CHeroSummonActor.Summoner` | Adds a one-level summon-to-summon owner lookup; original only resolves player GUIDs. | Summon ownership feeds shared bonuses/items/statistics. The supplied getter depends on the intermediate summon's already-populated cache, so it is not a general safe ownership reconstruction. | Exclude shared ownership changes. |
| `Patch_DebugMenu` / `DebugMenuProvider.Awake` | Suppresses intentional release-build destruction to enable developer tools. | New feature, not an existing game bug. The VR mod already has explicit opt-in cheats. | Exclude. |
| `FixModSaveName` / `PartyAdventureData.PartySaveDir` | Current native `PartySaveName` already emits the corrected mod marker format. | Broad replacement over the complete path can redirect legitimate existing saves/root or party names without migration. No verified current fix. | Exclude; no save is renamed or rewritten. |

The concrete extra-turn source is the shipped Beast Tyrant solo item
`Item_StaffOfCommand.yml` (item150), not a Three Spears item. Its during-own-turn
`CurrentAction` grant shares the audited native restore path. This UI correction
does not repair that item's separate native turn-stack problem or unsupported
other-player borrowed-hand contexts. Those would require shared-rule or broader
native flow work and are not presented as cured by a local UI prefix.

The bootstrap also appends `B5.0` to native `NetworkVersion.Current`; upstream
requires every multiplayer participant to install its mod. That assignment is
not imported. `PrintHelper` is an unused diagnostic helper, not a fix, and is not
imported either. These account for the non-patch code as well as all eleven patches.

Individual native analysis and proof boundaries:

- [Items and native replay](BUGFIX-ITEMS-AUDIT.md).
- [Extra turns, ownership and damage rules](BUGFIX-RULES-AUDIT.md).
- [Achievements, save paths, version branding and debug provider](BUGFIX-MAP-AUDIT.md).

## Mixed-client contract

The adopted runtime changes run only while VR is active and only repair local
native UI context. They never suppress original replay/restore, synthesize game
actions, change action/token fields, redirect an inventory, alter native version
negotiation, or patch `ScenarioRuleLibrary`. The original game remains responsible
for item execution, turn stacks, saves, ownership and shared commands.

An unmodified host or client receives the same native game action and token it
would receive without these repairs. It does not need another plugin or the
original Bug Fixes package. It still executes its own original UI and may still
encounter an original client-side game bug; fixing one VR endpoint cannot patch
another participant's unmodified process. Source/native proofs are not a real
connected mixed-session headset test. The latter still covers VR host/flat client,
flat host/VR client, quest-item use, extra-turn restoration and ordinary native
save/load. No all-peer rule fix is disguised by keeping the original version string.

`NetProtocol.ModBuild` is the VR-to-VR presentation handshake, separate from
native `NetworkVersion.Current`. Its normal651 bump does not register a vanilla
peer as an incompatible VR peer: existing `VersionGuard` tracks participants
only after their mod packets. Vanilla never sends those packets. Existing
cosmetic side actions still use the vanilla-ignored sentinel target and the
originally registered `CustomDataToken`; their grammar and transport are untouched.
Vanilla participants continue to see their ordinary flat UI, not another process's
VR-only NPCs or physical widgets. Their native shared purchases/item actions and
scenario progression remain the original game transactions.

## Integration and validation

Build651 follows the independent Build650 CPU integration and retains649 wrist
sliders,648's renderer rollback and646 NPC repairs. No config key, visual asset,
wire layout, renderer or Frame profile is added or changed by this audit.

Passed integration evidence:

- **Items:** original Unity methods, final58 production assertions, including a
  throwing diagnostic sink. Six causal controls from the prior57-assertion fixture
  remain inherited unchanged-runtime evidence; they are not relabeled as a final58
  complete-variant run. Exact production SHA256 is
  `434bceab36c96eb50b3e6b05446e7dbad1be69bf7d34d7ff0914f65c7ca314cd`.
- **Cards/rules audit:**542 production assertions, two effective causal negatives,
  and25+25 member-failure/missing-member assertions against original Unity methods.
  Exact runtime source SHA256 is
  `b5b794b9755fecf126fc39e69efd0d0dbacb41d037cfed282cf75d41a599b40d`.
- **Achievement audit:**8 assertions in unchanged original managed DLLs; this
  demonstrates the defect and divergent persisted counters, not a shipped map fix.
- Final registered-source group **16/16**, including original patch inventory,
  receiver classifications, source-order, surface/identity and bank contracts.
  Inventory contains206 patch classes /304 patched methods. Saved source surface
  remains663 config keys /4790 log tokens, with235 Harmony source entries versus
  baseline232 and no removals. These source entries are not a patch-class count.
- Strict **Debug and Release:0 warnings,0 errors**; all **299,714 direct wire
  assertions** pass. These are the golden executable, not another invocation of
  the complete `scripts/wire-tests.sh` local-suite wrapper.
- The test scheduler's18 tests pass, with2 optional resource-boundary skips.
  Discovery is now179 local /93 CI /16 source suites. New original-game fixtures
  are local-only because they require executable proprietary game/DLC inputs.
- All5 bilingual documentation pairs pass; author/URL are present in both READMEs.
  The copied MIT file is byte-identical to the pinned source licence and both
  archive builders' unchanged all-text licence loops include it.
- Private compiled comparison: **1238→1242 types**, four intended local UI/helper
  additions, no removals. `CompatModule` changes only by the three registrations
  and compiler local renumbering. The inherited `WorldMaterialBudget` is byte-exact
  to the compiled650 snapshot. The other seven changed types and `NetProtocol`
  differ only by the normal649→651 constant propagation. No receiver, mirror,
  rendering, native action serializer or unrelated behavior changed in651.

Successful native worker evidence is reused after proving both integrated runtime
source hashes unchanged. No already-passing native suite or650 material benchmark
is repeated. This is a **bounded integration pass**, not a new full179-suite gate.
Earlier648 composite/649 focused/650 focused receipts retain their original scope.

Main-checkout archive: `.planning/debug/bugfixes-audit/`, with `item-lane/runtime/`,
`rules/`, `map-lane/`, and final `integration651/`. Original failed fixture attempts
remain archived. Integration also retains two tooling failures: direct golden
apphost launch initially lacked `DOTNET_ROOT`; execution was repeated with the
installed runtime, without rebuilding. A generated snapshot-only helper initially
derived its root from its debug-directory location; its root binding was corrected
before the actual snapshot. Neither failed attempt is counted as a native pass.

The snapshot-only path uses the existing guard's unchanged build/decompile/masking
function and the private649 baseline; it avoids triggering an unrelated complete
catalog run. Runtime proofs do not establish headset visuals, a live mixed-client
session or immunity from unrelated native bugs. No new hardware capture was supplied
for this integration.

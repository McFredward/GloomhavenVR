# Bug Fixes 5.0.0: map and local patch audit

Audited from dev `f006ecf88` on 2026-10-09. The supplied DLL and the release
source have equivalent behavior for the four entries below; omitted argument
`OverrideLock` defaults to `false`. The externally supplied DLL/source snapshots
are read-only audit inputs in `.planning/debug/bugfixes-audit/`.

Upstream: [Bug Fixes by fingoldfish](https://www.nexusmods.com/gloomhaven/mods/8?tab=description),
[release source by gummyboars](https://github.com/gummyboars/gloomhaven-bugfixes).

This lane adopts **no production patch**. A defect's existence does not establish
that the supplied replacement is safe with unmodified players. In particular,
leaving `NetworkVersion.Current` unchanged cannot make replicated state edits
safe; the original mod changes that version and explicitly requires every peer
to install it.

| Supplied patch | Native defect / purpose | VR and unmodified-peer result | Decision |
| --- | --- | --- | --- |
| `UINewAdventureResultsManager.ShowNewAdventureModeResults` achievement prefix | Confirmed missing early achievement evaluation: Guildmaster per-round progress depends on transient scenario stats which the results UI clears. Exact shipped Demolitionist combo condition reproduces lost progress. | UI itself can be shown in VR, but the prefix writes serialized achievement counters, can transition achievement state/roll rewards and emit completion messages on every client, without host/mode/opening guards. The same input counted twice increments twice. Modified and original callbacks yield different serialized counters. No complete native host synchronization proof covers this new transition. | Exclude the upstream prefix. Do not invent a host-only reward command or claim safe crossplay. |
| `PartyAdventureData.PartySaveDir` replacement | Current native `PartySaveName` already generates `Campaign_[MOD]Ruleset[MOD]_Party_account`, with no extra separator after the first marker. The alleged obsolete format is not generated for ordinary current ruleset names. | Vanilla saves need no redirection. Upstream applies a global replacement to the *complete path*, including root and user names; a legitimate `_[MOD]_` substring there redirects existing files/checkpoints without migration or recovery. This is not a native multiplayer command, but that alone does not establish save safety. | Exclude: not a verified current fix; preserve all native save paths and existing files. |
| `DebugMenuProvider.Awake` replacement | Native intentionally destroys the debug provider in this release. Upstream initializes it to expose a developer feature. | It is not a repaired gameplay defect; can introduce debug actions outside the mod's existing opt-in cheat flow. | Exclude feature activation. |
| `MF.SetVersion` postfix | Adds `(Bug Fixes 5.0.0)` to the displayed version. | Merely branding, no defect correction. Copying it would misidentify this VR plugin. | Exclude decoration. |

## Achievement causal evidence

Current native source chain:

- `UIResultsManager.Show(...)` calls the original `CurrentAdventureData.ScrapeStats`.
- `StatsDataStorage.ScrapeEventLog(..., endScenario: true)` populates original
  `MapParty.LastScenarioStats` and native cumulative character statistics.
- `UINewAdventureResultsManager.ShowNewAdventureModeResults` checks trophy
  achievements, shows accomplishments and clears `MapParty.LastScenarioStats`.
  It does not check non-trophy achievements there.
- `PartyAdventureData.EndCurrentScenario` subsequently invokes
  `Choreographer.CheckAchievements`; map loading also checks them via
  `CMapState.CheckLockedContentInternal`. The per-round source is now empty.
- The shipped DLC_JoTL Guildmaster ZIP entry
  `Achievement/Achievement_Demolitionist_2.yml` requires `DemolitionistID`,
  attack damage, the same enemy three times in a round, fifteen occurrences,
  `RoundNoReset`. This is the C-C-C-Combo trainer goal named by the upstream
  changelog. The other two Demolitionist goals use different targets.

`scripts/check-native-bugfix-map-audit.py` loads unchanged native
`MapRuleLibrary.dll`, `ScenarioRuleLibrary.dll` and `SharedLibrary.dll`; no native
method is rewritten. It verifies the exact shipped YAML target before execution.
Eight assertions establish:

1. Three valid same-enemy attacks followed by the original results clear yield
   zero native combo progress.
2. Calling native evaluation before that clear yields one, retained by the later
   empty-map check.
3. Two attacks, or three attacks against different enemies, yield zero.
4. Repeating the unchecked early call over identical input yields two.
5. Original and modified callback ordering yield zero versus one; those counters
   are retained by original `CUnlockConditionState.GetObjectData` serialization.

The fixture explicitly prepares native party containers, an already unlocked
achievement and synthetic battle stats. Native filtering uses the original
`StatsDataStorage` type convention: recipient/source types are `Enemy`/`Player`
although the class/GUID property names might suggest the reverse. The full result
UI, platform rewards and a real Bolt session are not executed. Therefore the
fixture proves the defect and a mixed-client hazard, **not** complete native
host synchronization safety or a shipped correction.

`CPartyAchievement.CheckAchievement` changes progress and state;
`UnlockAchievement`/`CompleteAchievement` can call `RollAchievementRewards`,
consume `AdventureState.MapState.MapRNG`, set timestamps, queue completion
rewards and send native client messages. Those messages are local map-library
notifications, not a new universally replayed game action. Merely adding
`FFSNetwork.IsHost` would leave already connected vanilla clients without the
corresponding result-time check. Adopting it safely requires a separate proven
native transaction design; this audit does not replace one with assumptions.

## Current save-name evidence

A fresh decompilation of the current `ressources/GH_Data/Managed/GH.Runtime.dll`
matches the retained `decompiled/GH.Runtime/PartyAdventureData.cs` and confirms
its three original getters: `PartySaveName`, `PartySaveDir`, `PartyMainSaveFile`.
`PartySaveDir` is only `Path.Combine(PartySaveRoot, PartySaveName)`.
`PartySaveFileName` remains the unmodified `PartySaveName + ".dat"`; upstream
only replaces the directory. The original `GlobalData.GetSaveNameInfo` parses
`[MOD]` delimiters during recovery; there is no supplied migration or
existing-directory preference. We neither rename nor write any save.

## Validation scope

Run the bounded original-DLL audit:

```sh
python3 scripts/check-native-bugfix-map-audit.py
```

Receipts include original assembly and rules archive hashes, fixture/runner hashes,
the exact original YAML and stdout. This is an audit suite with eight native
assertions, not a complete local gate, Unity presentation test, achievement-reward
transaction proof or hardware result. No production/source, config, wire or
version change belongs to this lane.

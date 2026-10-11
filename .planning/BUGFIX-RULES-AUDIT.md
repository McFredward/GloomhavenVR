# Bug Fixes 5.0.0: extra-turn and shared-rule audit

Audit date: 2026-10-09. Baseline: `dev` at `f006ecf88` (ModBuild 649).
Upstream: gummyboars, [Gloomhaven Bug Fixes](https://github.com/gummyboars/gloomhaven-bugfixes),
[Nexus mod 8](https://www.nexusmods.com/gloomhaven/mods/8?tab=description).
This audit covers the four targets assigned to the rules worker; the integration
ledger covers the remaining shipped patches. The supplied 5.0.0 DLL and upstream
source snapshots are under `.planning/debug/bugfixes-audit/` in the main checkout.

## Decision

| Original patch | Real native defect? | Adopted here | Unmodded multiplayer |
| --- | --- | --- | --- |
| `CardsActionControlller.RestorePhase` | Yes: the restored local controller retains the extra turn's private restriction | Only refresh the private UI `extraTurnType` in owner-matching cached choice phases | Native rules, actions, network version and replay remain unchanged |
| `GameState.StartActorExtraTurnImmediately` | Source confirms the duplicated `CurrentAction`/resolved-side stack path behind item 150 | No | Rewriting this stack changes deterministic turn processing on only the patched peer |
| `CPreventDamageActiveBonus_PreventAndApplyToActiveBonusCaster.OnPreventDamageTriggered` | Yes: redirect can run while the optional bonus is off | No | Redirect changes actual damage resolution; vanilla and patched peers can disagree |
| `CHeroSummonActor.Summoner` | Yes: a summon parent is absent from the player-only lookup | No | Owner identity affects native gameplay; the upstream replacement also depends on a warmed parent cache |

Do not import the upstream multiplayer version suffix. Its README requires the mod
on every peer, and its plugin changes `NetworkVersion.Current` accordingly. That
contract conflicts with the maintainer's explicit requirement to support vanilla
clients. Excluding the shared-rule changes is deliberate, not a claim that their
underlying bugs do not exist.

## Local card-selection repair

Original references are from `decompiled/GH.Runtime/` in the main checkout:

- `CardsActionControlller.cs:91`: `Init` obtains the extra-turn restriction for a
  new hand. `CardsHandUI.cs:1504` calls it with the native actor's current type.
- `CardsActionControlller.cs:506`: `CachePhase` caches the original card pair,
  selection references and phase, then clears the active phase.
- `CardsActionControlller.cs:518`: `RestorePhase` restores those references and
  invokes original `SetPhase`, without restoring `extraTurnType`.
- `CardsActionControlller.cs:220`: original `SetPhase` owns the final allowed
  halves, native card ownership, played-side flags and disable bonuses.
- `CardsHandManager.cs:679`: restoring the phase simply delegates to that method.
- `Choreographer.cs:8020`: extra-turn selection finishes and caches the interrupted
  phase. At `8036`, the end-extra-turn message restores the phase before assigning
  the choreographer's current actor. The native `GameState` actor is already
  restored by `GameState.EndExtraTurn`, independently of that presentation field.

For the same actor's interrupted turn, an extra-card `Init` leaves the private UI
restriction stale after the extra-turn stack has returned to its previous value.
In `Select2ndCard`, that stale top/bottom restriction can disable the remaining
legal half. The proof executes the publisher's real `Init`, `CachePhase`, `Reset`,
`RestorePhase` and `SetPhase` with actual native card widgets and Unity buttons:
both opposing legal-half cases fail without the repair and pass with the real
Harmony prefix.

Concrete shipped content: `DLC_Solo/DLC_Solo_Global.ruleset`,
`ItemCard/Item_StaffOfCommand.yml`, identifies item 150 as **Staff of Command**, the
**Beast Tyrant** solo item (class #17), not a Three Spears item. It uses
`CurrentAction`, filters self, and requires a performed `ControlActor` ability in
the current action. That gives a same-actor interrupt after one selected card
action, the relevant cached second-choice context. Item 51, Ring of Brutality,
and item 42, Ring of Haste, supply top/bottom extra-turn restrictions, but their
end-of-turn trigger may have no cached choice phase; this repair does not claim to
fix every such item or complete an entire item-150 turn.

The bounded prefix refreshes only the UI field from the native actor after all of
these identities agree: active VR, native singleton controller, cached choice
phase, current native player actor, current hand owner and each cached card owner.
It leaves `Pick1stTarget`, `Pick2ndTarget`, `None`, observer clones, absent managers,
empty/stale pairs and other-actor borrowed hands untouched. The current hand is not
switched back before native restore; therefore the different-actor borrowed-hand
case is explicitly outside the repair's proven scope.

The upstream blanket `SetInteractable(true)` is omitted. In original choice
phases, `SetPhase` already resets and applies every native restriction. In
`Pick2ndTarget`, it does not re-disable the first card: running the upstream reset
on the real native widgets makes the already-used card clickable. The proof
contains this concrete counterexample. We preserve native target-phase behavior.

Optional owner-member lookup and singleton reads fail open. A missing member
declines the repair. A runtime reflection failure reports at most once, with a
protected logger, then original `RestorePhase` continues. Three consecutive
restores were tested with both an incompatible owner field and a logger that
throws. No native continuation is suppressed by the prefix.

## Shared extra-turn stack: excluded

Native references are under `decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/`:
`CAbilityExtraTurn.cs:71`, `GameState.cs:3600`, `3645`, `3693` and `3704`.
`CAbilityExtraTurn.ApplyToActor` records the requested `CurrentAction` in the
selection field but resolves the actual action to a top/bottom type and places
that resolved value on the actor stack. `StartActorExtraTurnImmediately` compares
the two values and can add `CurrentAction` as a second marker. End-extra-turn
processing pops markers, restores cached action selection and subsequently
chooses extra-turn versus ordinary discard behavior using native turn state.

The upstream postfix removes `CurrentAction` when a resolved side lies beneath
it. This is a shared-model change, not a local widget repair. A vanilla host with
a VR client, a VR host with a vanilla client, and replay of vanilla actions would
not execute the same stack transitions. No safe local UI substitute cures the
shared crash. The upstream README itself documents an unresolved case: an extra
turn granted by another player followed by item 150 after only one card action.
Do not advertise the adopted UI subset as the full item-150 crash fix.

## Divine Intervention: excluded

`CPreventDamageActiveBonus_PreventAndApplyToActiveBonusCaster.cs:13` does not test
its own optional toggle before assigning redirected damage.
`CBespokeBehaviour.cs:158` and `197` subscribe the behavior to actor-wide damage
prevention listeners. Another active shield/prevention bonus can broadcast the
event (`CShieldActiveBonus.cs:44`, `CPreventDamageActiveBonus.cs:35`) even while
Divine Intervention itself is toggled off. `GameState.cs:1466` then applies the
redirected damage to the caster.

Shipped `AbilityCard/204_Sunkeeper_DivineIntervention.yml` is Sunkeeper level 9,
with the redirect behavior and an optional toggle. The actual publisher behavior
was invoked in Unity with a real untoggled bonus and a native actor: prevention
of three with strength one still produced a two-damage redirect. The zero-net
damage control did not redirect. This confirms the defect without installing any
ScenarioRuleLibrary patch. Adding the upstream toggle check would change actual
health/damage outcomes on only modded peers, so it is excluded.

## Summon of a summon: excluded

`CHeroSummonActor.cs:116` resolves its parent GUID only against `AllPlayers`.
`CAbilitySummon.cs:423` can record a summon actor as the parent. In that case the
getter returns null and `IsCompanionSummon` (`:133`) dereferences it. This matters
to summon-enabled custom content; this audit does not invent a stock card that
uses that configuration. Actual publisher objects in Unity reproduce the null
getter and the `IsCompanionSummon` exception with a valid player/parent/child chain.

The upstream replacement looks in `AllHeroSummons`, but reads that parent's
private cached player instead of resolving its getter. A cold parent cache still
returns null; warming the real parent getter changes the cached result. Deeper
chains and lifecycle-invalid parents are not solved by that shortcut. Summoner
identity also affects initiative (`GameState.cs:102`), death handling and
bless/curse ownership, so replacing it is a deterministic gameplay change. No
local presentation getter can safely fix all those native consumers in mixed
multiplayer. Excluded both for the protected rule boundary and incomplete coverage.

## Runtime evidence and practical limit

Runner: `scripts/native-bugfix-cards-runtime/run.py`. It compiles the production
prefix, executes publisher GH.Runtime methods in Unity 2021.3.5f1, and uses real
native `FullAbilityCard`, half-action buttons and controller instances. Transport
and the pre/post native actor-stack context are explicit fixture boundaries;
neither the controller methods nor the native damage/summoner methods are copied
or replaced. The full game startup/message pump is not booted.

Final receipt: `run-y3k8r_g9/results.txt` under the worker's ignored native-runtime
output directory, archived to the main audit directory before integration:

- **542 production assertions**, including two native baseline failures; 48
  choice restores across four transport-role boundaries, both first-card sides
  and six restriction/ownership conditions; first-choice/single-card controls;
  target-phase counterexample; and native damage/summoner counterexamples.
- **Two causal negative controls**: removing only the private type refresh fails
  the legal-half check; dropping cached-card ownership fails the stale-pair guard.
- **25 + 25 fail-open assertions** for incompatible and missing owner members.
- Native actor types/stacks, selection and played flags, current actor and action
  selection stack remain identical. No native `GameAction` is generated by repair
  or restore. Only the GH.Runtime restore prefix is patched; ScenarioRuleLibrary
  methods remain original.

The four role labels are singleplayer, VR-host with vanilla client, VR-client with
vanilla host, and VR replay of a vanilla action. They vary real native online
guards at an explicit transport boundary; they are **not connected multiplayer
sessions**. The native action schemas and network version are unchanged, but
paired-client and headset verification are still hardware work. Earlier failed
fixture experiments are retained separately and are not counted as passes.

All five case assemblies compile with warnings treated as errors. One-off runner
projects now remove generated Unity `Library`/`Temp` in a `finally` block; explicit
`--reuse-project` preserves its caller-selected cache. Receipts, input hashes,
variant assemblies and `unity.log` remain available. This cleanup-only adjustment
was checked with temporary directories rather than repeating the unchanged native
runtime proof.

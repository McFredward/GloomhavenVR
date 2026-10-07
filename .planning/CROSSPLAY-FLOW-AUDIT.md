# Flat/VR crossplay flow audit — 2026-10-07

Source baseline: `dev` `819a9a9ee`, ModBuild 638. This independent lane read the
native game sources and the mod's presentation/continuation boundaries. The
mission-start report contains no paired logs, build banners or screenshots; it
is a user observation, not hardware evidence for this baseline. Existing NPC or
Frame captures must not be attributed to the reporting user's session.

## Findings requiring repair

1. **Quest proposals and browsing share a selection observer.** The native host's
   `SelectQuest` becomes `UIMapMultiplayerController.HostSelectedQuest`, independently
   of client browsing. `RemoteMapRoom.ResolveSelection` (750–799) nevertheless
   adopts every new peer browsing edge. `MapLocationInteractor.AdoptSelection`
   (174–192) checks the native map lock, which has not yet been acquired by an
   unready participant; it can therefore replace or clear that participant's
   browsing selection during a native proposal. `TickQuestDecision` (1574–1609)
   forwards this browsing identity to the readiness observer. Repairs must use the
   native host proposal as the client's decision identity and keep cosmetic
   selection from cancelling/replacing its live confirmation. Map rebuilds,
   ordinary native previews and repeated equal proposals must not synthesize a
   new human browsing edge. This is in the selection worker's lane.

2. **The native multiplayer quest popup is a distinct original instance.**
   `UIQuestPopupManager` owns `selectedQuestPopup`, `questPreviewPopup` and
   `multiplayerQuestPopup` separately (native lines 8–15). `ShowMultiplayerPreview`
   (179–186) shows the third object. Modal discovery, shared identity, close
   handling and confirm parking must follow the actual original instance rather
   than assume that one serialized `QuestPopup` ID identifies all of them. The
   root/ready-up lanes cover discovery, lifetime and parking. The reported brief
   Accept appearance and rejoin recovery are consistent with a lost presentation
   lifetime, but source review alone does not establish the reported session's
   exact cause.

3. **Optional retirement of another player's campaign character can strand the
   map.** Native `UIMapMultiplayerController.ConfirmRetirement` (849–878) creates
   a pending promise when `isOptional && !character.IsUnderMyControl`. Only
   `GuildmasterConfirmAction.ShowCharacterRetiredAction(..., promise.Resolve)`
   resolves that promise. Both concrete presenters show a HUD child, not a
   `UIWindow`, and there is no retirement-prompt bridge at this baseline.
   `ModalFallback.10.CatchAll.cs:IsKnownHudWindow` (593–618) excludes the original
   guildmaster HUD; the map's desktop composite is suppressed while the room is
   active (`FlatScreen.4.Lifecycle.cs` 126–135). The notification at native
   `UIPersonalQuestResultManager` 151–165 only recycles its content, never resolves
   the retirement promise. `MapChoreographer.QueueConfirmRetirement` (3533–3558)
   retains `QueuedRetirements` until that promise finishes and disables the city
   event; `WaitAllRetired` (2457–2463) waits for the queue. A missing optional
   prompt therefore becomes a shared retirement barrier. A real player press on
   the original visible/pokable prompt must remain the only action that enters
   native `PlayerConfirmRetirement`; automatically resolving it would skip the
   optional choice. This audit lane owns the repair.

## Authority and continuation trace

| Flow | Native continuation and quorum | VR boundary reviewed |
|---|---|---|
| Campaign/Guildmaster quest | `ConfirmSelectedLocation` emits native `SelectQuest`; native client `ProxyHostSelectedLocation` previews it; `UIReadyToggle.ReadyUpPlayer` emits native readiness; host ACK barrier emits `ReadyProceed` | Quest content, popup lifetime and accept parking require the repairs above. The mod must not invent a second quorum or send a native quest on behalf of a Flat host. |
| Linked scenario / return to headquarters | Native `AtLinkedScenario` follows the direct `PreviewQuest(isCancellable: false)` branch; native host's `ConfirmTravel` chooses the destination | Keep the direct linked branch and original cancellability. Root/ready-up tests must include return-to-HQ as well as ordinary quest nodes. |
| Travel intro/outro | `MapStoryController.Show` receives original travel callbacks; native story completion resumes party movement (`MapChoreographer` 1804–1898) | Map-story lifecycle advances only a matching original opening; local original input remains available without Mod packets. |
| Road/city encounter | Native host's `EventButton` produces `ContinueRoadEvent`; Flat-host clients retain vanilla disabled option controls | `EncounterChoice.HostCanHonourRequests` (162–166) requires a modded host before VR clients receive request controls. `MapCityEventSource.Pressable` retains original campaign permission, map lock and HUD request set. |
| Loadout / battle goals | Native `UILoadoutManager.MPConfirmEnterScenario` (474–516) requires original loadout validity and native ready quorum; only host runs `ConfirmEnterScenario`. Native battle-goal picker retains `characterData.IsUnderMyControl` | World presentation uses original widgets. `LoadoutConfirmPark` separates the loadout toggle from `Quests`; native battle-goal choices remain private/control-scoped. |
| Scene entry | Native host sends `EnterScenario`; every client invokes original `ClientEnterScenario` and native scene-loading readiness (`MapChoreographer` 2230–2255, 2975–3093) | No mod avatar, shared pose or handshake is an entry prerequisite. Native loading/ACK failures remain native failures; a source audit cannot promise network delivery. |
| Scenario story | Native `StoryController` original page/end callbacks release original processing locks | `MapStoryLifecycle.ResolveBox` applies only a matched original opening; it retries a failed final dispatch only while the same native opening remains open. |
| Scenario reward showcase | Campaign original Continue emits native `ConfirmReward`; `UIRewardsManager.ProcessRewards` retains controlling player/host authority and emits `ProcessNextReward` | `RewardShowcase.CanConfirm` (64–83) uses original active/interactable controls or original `interactionChecker`; it never uses reward-pose authority as gameplay permission. |
| Post-quest reward / public introductions | Each peer's native reward/intro callback advances its own queue; original final multiplayer ready barrier remains native | `PostQuestRewardSync` retains exact opening provenance, native reveal/button eligibility and completion history. Distribution, unlock videos, retirement choices and saves remain native. |
| Reward distribution | Native `UIDistributeRewardManager` continues through its original choice callbacks | Existing mandatory-decision policy prevents hiding a required decision. The existing map rescue exposes native UI if distribution has no reachable converted surface. |
| Character assignment | Native host retains assignment authority; Flat-host clients retain vanilla disabled assignment controls | `AssignmentChoice.MayOperateHere` (326–333) grants a VR client adapter only when the host can receive it. No sentinel request to an unmodded host is represented as an actionable control. |
| Enemy information / action phase | Native host Continue broadcasts `ConfirmAction` | `EnemyInfoContinue` offers a VR client request only for a modded host. Under a Flat host the host's original Continue remains the continuation. |
| Retirement | Original confirmation, original retirement ready-up and commit callbacks | Mandatory confirmation is already original; optional observer prompt requires repair finding 3. |

## Flat peer absence is not a mod continuation barrier

`VersionGuard.CollectContinuationPeers` (135–146) collects only connected roster
entries whose observed build equals `NetProtocol.ModBuild`. A native Flat peer
sends no GVR1 packet and does not enter that set. `MapStoryOpeningLedger.Resolve`
(189–222) consults existing matched VR bindings; absent bindings neither block a
local button nor create a required vote. Shared story completion is additional
VR presentation/continuation behavior. Flat peers click their own original story
pages and retain the game's native barrier semantics.

Reward first-reveal authority likewise excludes Flat peers.
`NetAvatarDriver.CollectRewardPosePeers` (702–710) requires a live VR avatar and a
supported observed build. `RemoteMapStory.Reward.cs:RewardInitialOwner`
(187–218) elects an available VR window, retains an established visible window,
and falls back to an actual surviving original window after departure/declines.
It does not reserve ownership for the native host ID. Therefore a Flat host
cannot be an absent pose author. `RewardShowcasePlacement.TryReveal` (47–61)
allows local reveal when no matching network authority exists. This gate affects
first-picture placement only; original reward confirmation permission remains
separate.

Choosing the version dialog's explicit Flat-net fallback clears remote avatars,
stories, map windows and reward state (`NetAvatarDriver.TickVersionGuard`
1161–1192); sending/receiving is then gated while local VR and native multiplayer
continue. Session shutdown resets the choice. Version mismatch with another VR
client behind a Flat host is still detected locally against that VR peer; a Flat
host is never itself treated as a build mismatch.

Native transport sources use `NetworkActionEvent.Create(GlobalTargets.Others)`
for ordinary side actions (`FFSNet/Synchronizer` 23–30), and native receivers call
`ActionProcessor.ProcessSideAction` (`NetworkCallbacks` 130–155). The sentinel
target is rejected before vanilla execution (`ActionProcessor` 175–194).
Client-to-client mod-presentation delivery through an actual unmodded host still
needs paired hardware evidence; it is not needed to show or accept that host's
native quest proposal.

## Host/player matrix and acceptance targets

The same native mission path applies in Campaign and Guildmaster. Display-mode
permutations reduce to the following authority/presentation classes; each row
must be exercised with both quest sources (map icon and original quest list).

| Native host | Quest selector / voter | Required behavior |
|---|---|---|
| Flat | Flat host selects; one VR client | VR sees the original current quest and its own native Accept; no mod host packet required. |
| Flat | Flat host selects; two VR plus one Flat client | Both VR participants see the same proposal; all native participants can vote independently; the Flat clients remain unmodified. |
| Flat | VR client browses while host has proposed | Browsing cannot substitute a different ready decision or cancel the host's live proposal. |
| VR, 3D map | VR host selects; Flat clients | Native host proposal reaches Flat clients through native `SelectQuest`; the VR host retains its original ready control. |
| VR, 3D map | Flat client browses; VR host selects | The host's native accepted proposal remains authoritative; a client does not gain host selection authority. |
| VR, 3D map | Other VR client browses/selects before host proposes | Existing explicitly authorized shared VR browsing remains available before commitment. Native selection/ready semantics remain host-owned. |
| VR, native 2D map | Flat or VR clients | Native desktop map/prompt remains reachable; 3D-room-only adapters stay inactive. |
| Either | VR client enables 3D room after host proposed | Recover the live original proposal/control level; do not require a new edge or lobby rejoin. |
| Either | Quest A → quest B / host cancel / repropose same quest | Only native proposal transitions clear obsolete readiness; original callbacks remain correct and current proposal stays reachable. |
| Either | Native map rebuild / city-world switch | Equal proposal identity and readiness survive object replacement; no synthetic human selection. |
| Either | Participant leaves / reconnects | Native `UIReadyToggle.OnPlayerLeft` removes its readiness/ACK/awaited entry, but the quest initializer at this baseline disables the later revalidation. The repaired VR-host initialization must enable that existing native validation. A Flat host remains unmodified; the genuine visible VR-client Cancel needs an explicit-user recovery for the full remaining-group guard. Rejoin discovers the current original proposal; no stale mod pose/opening may answer a new occurrence. |
| Either | Spectator / zero local characters | Native `Participant` quorum is distinct from `Player` quorum. Display a native proposal where appropriate; never manufacture a participant vote or require all roster entries for a participant-only quest. |
| Either | Mixed VR versions, chosen Flat-net fallback | Native mission proposal and Accept still work locally; no mod-only quorum, pose or host-request requirement. |
| Either | Optional remote retirement | Original prompt stays visible and waits for a real press; its successful native callback enters the original retirement ready barrier. |

Native source proof and focused causal fixtures establish control/continuation
boundaries, not headset picture correctness. Final integration must record the
actual focused suites, controls, source checks and golden vectors run against
the repaired tree. It must distinguish inherited unchanged evidence from a new
complete gate and retain hardware-unverified status for the matrix until actual
Flat/VR sessions test it.

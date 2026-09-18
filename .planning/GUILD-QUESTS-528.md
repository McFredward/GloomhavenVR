# Permanent map quest list after a temporary story curtain — build 528

## Evidence and flat behavior

The supplied local test is release 1.0.4 / ModBuild 527. The remote log is build 500 and
does not establish current multiplayer behavior. Local `LogOutput.log` shows the original
Quest Log Manager float leaving at 263, 1495 and 3048; the story refusal is explicit at
297, 1528 and 3081. A new float occurs at 1613 in the second visit. The first and third
visits have no equivalent new quest-list float before travel. The current log level does
not retain every native Hide request, so it does not identify the precise requester of
each disappearance.

The flat game does **not** keep this window permanently visible:

- `QuestManager.OnMapLocationQuestSelected` calls `HideLogScreen(this)` for a selected
  quest and `ShowLogScreen(this)` on deselection (native lines 140–165).
- `QuestManager` maintains a set of independent hide requesters (201–212).
- City events, reward distribution, map tutorials and story messages also request hides
  (`MapChoreographer` 1127/2080, `UIDistributeRewardManager` 87, `MapFTUEManager` 118).
- `QuestLogManager` delegates to its original `UIWindow.Show/Hide` (311–319).

VR already intentionally keeps the original quest-list widget standing during normal
map browsing, without an X. Quest-start story/loadout/travel remains the earlier approved
temporary exception. This change restores that existing VR behavior after the exception;
it does not keep the list on top of the committed quest sequence or force native flow.

## Source-proven gap and fix

Permanence was held exclusively by `WindowPanel.Sticky`. A temporary float refusal
destroys that panel. Meanwhile native Hide removes the window from `UnknownShown` and
`Open`. Once the refusal ends, there is no mechanism to restore a still native-hidden
quest list: native Show may not occur until a later deselection. The curtain's comment
promising automatic return was therefore incorrect for this state.

The refusal-release path now remembers only a live original Quest Log Manager that
actually had a successful float. It does not remember arbitrary hidden windows, new
undisplayed quest logs, user closes or empty-content releases. After all refusal and
point-of-no-return conditions end, the normal poll/conversion path enrolls that exact
original widget independently of native `IsOpen`. Existing sticky presentation handles
visibility. No native Show/Hide, hide-request set, quest callback or gameplay state is
written. Successful conversion consumes the single pending return. Destruction, room
exit and module detach clear it, preventing stale scene objects from returning.

This applies to both map modes because they share the same native Quest Log Manager
and the same permanence contract. It has no new remote widget or network state.

## Validation

- `scripts/permanent-quest-log-tests.sh`: 26 production-linked assertions, five binding
  checks and four rejected mutations (native-closed loss, story exclusion loss, wrong
  return consumption, missing room-exit reset).
- Worker strict Release build: zero warnings and zero errors. Full integrated gates remain
  the integrator's responsibility.
- Hardware still required: in Guildmaster, finish introductory/story messages, browse
  quests, select/deselect, open/close destinations, return from a scenario and repeat.
  The quest list should return to normal browsing, stay nonblocking, and still withdraw
  during committed quest/story/loadout flow. Automated tests do not establish the HMD view.

Integration completed: the production harness runs through `scripts/wire-tests.sh` in the
local guard and in the hosted development presentation checks.

# Guildmaster browsing versus accepted quest story — build 530

## Evidence and user ruling

Local hardware evidence is ModBuild 529, assembly 1.0.5.0. The remote capture still names
build 500 and cannot establish current multiplayer behavior. Local LogOutput lines 2589,
2593 and 2602–2604 show the story curtain closing the room, withdrawing the quest list,
then refusing its return. The user reports a persistent Guildmaster dialog during ordinary
browsing and clarifies that it must not hide the quest list. After actual quest acceptance
(point of no return), the quest story must hide it normally. This is not an unconditional
always-visible exception to accepted-journey presentation.

## Source repair

`MapStoryController.isVisibleOtherUI` alone was treated as proof of quest commitment.
Guildmaster's ordinary dialogs can also request HideOtherGUI. The story classifier now
requires either the measured native party confirmation, an accepted native journey phase
(Moving, RoadEvent, AtScenario), or the actual loadout stage before a Guildmaster dialog
can raise that curtain. Campaign behavior is unchanged. The existing native message flag
and standing-window checks remain required; no native callback or quest state is changed.

The native phase fallback matters for multiplayer: MapChoreographer.ClientMoveToNode
calls OnMoveClick directly without the selected-location sample used by PartyCommitted.
The native rule client creates Moving on travel and AtScenario after arrival; EnterScenario
opens loadout. AtLinkedScenario is deliberately excluded because native victory can set
it before the player chooses a linked quest.

The original QuestManager.questLog window is discovered even if native Hide preceded its
first Show. During visible Guildmaster map browsing, keep that same window enrolled across
native Hide and temporary presentation withdrawal. Respect all real story/journey refusals;
stop enrollment when the parchment becomes hidden, and clear the reference on room exit.
Campaign retains its existing one-shot deferred-return policy. There are no cloned widgets,
new gameplay controllers, native Show calls, or native IsOpen writes.

## Validation and hardware limits

The production-linked permanent-quest-log harness passes 69 assertions, 11 source bindings
and five rejected negative controls. It covers native-closed discovery, persistent reuse,
ordinary dialog versus committed journey/loadout, hidden/inactive parchment, campaign
return, conversion disabled, destruction and room exit. Integration checks are recorded in
STATE.md after the complete guard finishes.

Headset checks: browse/select/cancel quests while the persistent dialog is present; accept
a quest and confirm the list disappears for the actual story/loadout; return to the map;
repeat on a multiplayer observer and verify campaign behavior remains unchanged.

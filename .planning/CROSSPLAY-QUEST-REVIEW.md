# Flat/VR crossplay quest and continuation review

Review started 2026-10-07 from `dev` `819a9a9ee` / ModBuild 638. Integration is
coordinated after the NPC and Steam Frame changes; final build and validation
receipts are recorded below when that common tree has been checked.

The report describes two VR and two Flat players, a VR host with no reachable
mission-start control, and a subsequent Flat host whose proposal briefly exposed
Accept before it disappeared. Rejoining made it reachable. No paired build
banners, logs or screenshots accompany this report. The source defects below
explain lost presentation and continuation paths; they do not establish which
one happened on the reporting user's installed build.

## Repaired source boundaries

The current quest now comes from the original native host proposal and selected
location. A Flat host sends the game's ordinary `SelectQuest`; no GVR1 selection
record or VR avatar is necessary to observe it. Client browsing cannot replace
or clear a live host proposal. Unknown native state during map replacement is
kept distinct from measured cancellation, including travel locations whose
location token differs from their quest ID.

Quest confirmation follows the native popup manager's exact original instance.
The native multiplayer popup is a proposal-hover view; invoking the original
preview callback switches to the ordinary selected-quest popup. Parking, shared
pose lookup, initial placement and release follow that current subject rather
than a serialized enum ID or the first popup of a type. Closing a hover cannot
retain it merely because a different selected quest is still present.

The original client preview callback is captured before entering the map room.
An already visible ready toggle is insufficient evidence that this callback ran:
native initialization can preserve a previous visibility request. The dispatcher
checks the exact controller, quest and `Quests` readiness state, preserves a live
pending prompt across presentation rebuilds, and consumes actual native desktop
answers so entering 3D later cannot replay them. The desktop callback retains the
native hover-cleanup wrapper. Invoking this preview does not vote; the player's
original Accept/Cancel still owns readiness.

The broader audit also found two continuation barriers: departure during quest
voting and an optional remote-character retirement promise whose original HUD
prompt was not presented in 3D. Their repairs retain native authority, native
readiness and ACK validation, and actual player input. No second quorum,
automatic retirement vote, or gameplay request to an unmodded host is added.

## Review coverage and practical limits

[The flow audit](CROSSPLAY-FLOW-AUDIT.md) traces Campaign and Guildmaster quest
selection, linked scenarios and return to headquarters, travel stories,
road/city encounters, loadout and battle goals, scene loading, scenario stories,
rewards, distribution, assignment, enemy continuation, retirement, version
fallback, and participant departure/rejoin. Flat peers do not join the mod-only
continuation set. VR presentation authority remains separate from the game's
host or character-control permission.

The regression fixtures exercise both Flat and VR hosts, original desktop and
console presenters, map icon and quest-list sources, mixed participants and
spectators, delayed map activation, two separate client worlds, quest changes,
reproposals, map rebuilds and rejoin. Read-only native references are checked
against the committed native fixtures where available; CI can run the tracked
fixtures without the game installation. Causal controls reintroduce the defects
and require a specific runtime assertion failure, not merely compilation failure.

Real HMD verification remains open for these sessions in both Campaign and
Guildmaster:

| Session | Visible quest and control target |
|---|---|
| Flat host + two VR + one Flat client | Both VR clients immediately see the selected native quest and can independently Accept; the Flat participants complete their ordinary votes. |
| VR host in 3D + two Flat + one VR client | VR host retains its native start control; Flat clients receive and accept the ordinary proposal; the VR client sees its corresponding original quest/control. |
| VR host/client changes 2D ↔ 3D after proposal | Live unanswered proposal becomes reachable; an already answered desktop prompt is not replayed. |
| Either host, quest A → B / cancel / same-ID reproposal | Obsolete presentation leaves; only the current native proposal can be answered. |
| Either host, unready participant leaves during voting | Native VR-host departure validation or explicit VR-client Cancel under a Flat host provides continuation/recovery before commitment. |
| Either host, optional remote retirement | Original prompt remains reachable and its promise advances only after an actual player confirmation. |

Automated source/runtime checks cannot prove headset placement, real transport
delivery through an unmodded host, or freedom from every game/network failure.
The repaired mod paths must remain reachable without a lobby rejoin; any new
hardware result must be recorded with both build banners and paired logs.

## Final integration evidence

Pending the coordinated final integration tree and complete local gate. Focused
worker receipts are recorded in [selection](CROSSPLAY-QUEST-SELECTION.md) and
[readiness](CROSSPLAY-QUEST-READY.md); these are not a new complete-gate pass.

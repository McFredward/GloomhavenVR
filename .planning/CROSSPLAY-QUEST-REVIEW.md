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

Quest initialization now enables the game's existing departure-validation
option only for online VR `Participant`/`Quests` readiness in `MapHQ` or
`MapAtLinkedScenario`. A VR host therefore runs the original remaining-participant
and controllable-state ACK checks. A Flat host remains unmodified. If a genuine
native departure leaves a VR client's existing Cancel visible and enabled while
its current native ready roster is full, only that player's actual Cancel input
may use the native `autoValidateUnreadying` argument. The game's original native
Unready action carries it to the Flat host's original validation. Scope ends at
that withdrawal; proposal/controller/phase changes, reset, commitment and cancelled
progress invalidate it. An asynchronous native cancel animation retains only its
own original input authorization. No automatic vote or separate quorum is added.

Optional observer retirement now presents the original HUD widget in 3D. Its
pending native promise advances only after an actual player press. A mod-owned
conversion wrapper keeps the original widget's native root animation intact;
closing or changing the room restores its current rect, parent, sibling order and
layers. A temporarily refused hierarchy restore retries without deleting the
original. A real conversion failure requests the original desktop HUD for this
prompt's lifetime, including the console layout's guarded explicit-click adapter.
The original retirement confirmation and ready-up remain native.

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
| Either host, ready/unready participant leaves during voting or ACK wait | Native VR-host departure validation or explicit VR-client Cancel under a Flat host provides continuation/recovery before commitment. Spectator departure grants no permission by itself; the same visible blocked-state checks still apply. |
| Either host, optional remote retirement | Original prompt remains reachable and its promise advances only after an actual player confirmation. |

Automated source/runtime checks cannot prove headset placement, real transport
delivery through an unmodded host, or freedom from every game/network failure.
The repaired mod paths must remain reachable without a lobby rejoin; any new
hardware result must be recorded with both build banners and paired logs.

## Final integration evidence

Pending the coordinated final integration tree and complete local gate. The
implemented boundaries have these focused receipts:

| Fixture | Executed checks | Causal controls |
|---|---|---|
| Native map selection and popup identity | 358 original/private + 358 publicized assertions; 18 native bindings | 9 |
| Native client quest readiness and departure | 224 prompt assertions + 63 departure assertions; 25 readiness layouts; 10 + 22 verbatim native bodies | 7 + 7 |
| Current quest window and shared pose routing | 160 production-linked assertions | 4 |
| Original quest-card placement | 45 assertions | 3 |
| Native optional retirement prompt and animation | 198 checks; 32 role/presenter cases; original promise/retirement flow, actual conversion-frame reassertion and native scale animation | 10 |
| Optional real HarmonyX 2.7.0 / Unity Mono | 72 assertions; all seven production departure targets and both scoped-state pairs installed; no manual seam dispatch | Not a portable CI requirement |

Selection and readiness execute with tracked native fixtures when the read-only
game reference is unavailable; workers verified both reference-present and
reference-absent execution. The integrated readiness, window and retirement
boundaries also passed focused root runs. Strict root Debug and Release builds
reported zero errors and zero warnings. The source group passed 15/15 before the
final departure addition; docs i18n passed all five language pairs. Final source,
golden and complete local evidence must still be recorded against the common
641 tree. These receipts are not a new complete-gate pass.

Detailed worker receipts are in [selection](CROSSPLAY-QUEST-SELECTION.md),
[readiness](CROSSPLAY-QUEST-READY.md) and [the broader flow audit](CROSSPLAY-FLOW-AUDIT.md).

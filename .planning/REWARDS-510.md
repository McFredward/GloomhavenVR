# Chest reward continuation and shared presentation — ModBuild 510

## Request and evidence

The maintainer reports an undismissable reward window after opening a chest and requests
that every multiplayer participant can see the reward in a shared window. The proposed
interaction is an explicit Continue button, operated by the native controlling player;
native game actions advance or close the reward for all participants.

The available local LogOutput.log reports ModBuild 507; remote/LogOutput.log reports 500.
These are earlier sessions and do not establish the cause of this new hardware report.
No screenshot of the new reward failure was supplied. The following defects are proven
from the native and mod source, not a reproduced current headset trace.

## Native continuation

GuildmasterScenarioRewardManager opens UIRewardsManager. Its ProcessRewards coroutine
reads InControl MouseClickLeft.WasPressed or isConfirmPressed. A VR uGUI laser/poke click
does not set either. A visible Continue control now forwards to native ConfirmPressed;
the existing coroutine retains reveal/advance behavior and multiplayer ownership checks.
It never calls MoveToNextReward, EndProcess or Hide from presentation code.

UICampaignRewardWindow installs its original Continue callback during Awake only in
mouse mode. A window created in gamepad mode can therefore have no pointer callback.
VR repairs that exact native runtime listener once without duplicating it or replacing
the original button. CampaignRewardsManager already makes completion idempotent by
clearing onConfirmed inside its initial callback before scheduling FinishCoroutine.
Native reveal animation and native button eligibility remain authoritative. A stalled
native reveal would be a different issue; no current source or supplied trace establishes it.

Campaign reward windows also explicitly match the mandatory-decision identity, so a
generic close chord cannot hide their presentation without invoking the continuation.

## Shared window

RewardShowcase is shared kind 6, using the normal shared badge, fixed design sizing,
scenario anchor and grab/resize behavior. The game already shows the reward content on
each client and synchronizes confirmation; the mod transports presentation pose only.
Additive record 73 leaves legacy shared record 21 unchanged.

The identity hashes the exact chest PropGuid from Choreographer.LastMessage's
CActivateProp_MessageData while the native message pump is blocked. It does not infer
identity from localized text, reward amounts, Unity instance IDs or the latest event log.
Thus two chests containing the same reward remain distinct. Unknown identity does not
publish a guessed pose and never prevents native local confirmation.

Scenario reward poses use the same absolute WorldAnchor as avatars, not a viewer's
panned/orbited seat frame. A reward-only frame value leaves legacy frame grammar intact.

The lowest live reward-capable VR participant supplies the initial pose. Pending geometry
can be transmitted while hidden; a Ready flag prevents followers from revealing an
unsettled placement. Followers apply that final pose immediately before first visibility.
A native conversion failure or screen-only presentation is advertised explicitly and another
participant takes over. The latter reuses ModalFallback's actual ConvertBaseActive policy:
a peer showing the manual screen cannot remain an elected publisher without a float.
Departure uses ordinary membership removal. An absent/disabled net module does not block
local confirmation or reveal. Later actual user movement takes priority, and omitted/stale
peer records retire their claim. No reward/character/card identity or continuation command
is introduced on the mod transport.

The first visible receive also seeds the reward interpolation track at the received endpoint.
Otherwise the next network tick could move the just-revealed window backward through its
hidden local spawn pose. The handoff survives identity-settle early returns; subsequent
visible movement retains ordinary smoothing. An already visible shared endpoint takes
precedence over a newly joined lower-ID participant's initial local placement.

## Validation

The integrated dev candidate passes the strict Release build with zero warnings/errors,
all 17 source/asset/surface checkers and the complete production regression suites.
Wire vectors: 253,759 assertions. Reward continuation/placement: 219 assertions and
11 deliberate runtime negative controls, including execution of the actual conversion
eligibility expressions. Reward wire coverage includes malformed records, omission,
ownership, ready/failure transitions, common coordinates and first-visible interpolation.
Existing card, flight, figure, four-board, map, video and tutorial suites all pass.

The retained build-502 compiled baseline (080c505e9) reports 34 changed types, 22 additions
and no removals. Its expected nonzero summary verdict represents reviewed implementation
changes, not a failed source checker. Compared with the pre-task build-509 review's
32 changed/14 added, the two newly changed types are SharedWindowFrame and SharedWindowKind;
the eight new types are the four reward WorldUI helpers and four reward transport helpers.
Config keys remain 625, patch signatures 161 and runtime patch inventory 117 classes /
184 methods. Log tokens increase from build 509's 4,724 to 4,726 for reward input/placement.
Bilingual documentation, shell syntax and whitespace checks pass. Hosted workflows invoke
the new production regression runner; hosted wire vectors retain their existing compile-only
limitation because they require the game's Unity runtime for execution.

Hardware acceptance remains open: confirm chest rewards in Campaign and Guildmaster,
locally and as the remote controlling player; verify original reward artwork, readable
Continue, shared badge/pose/resize, disabled observer input, sequential identical rewards
and successful continuation into the next action. Include late observers and flat peers.

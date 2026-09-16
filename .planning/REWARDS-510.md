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

Late native windows can adopt an already elected peer pose before reveal. Initial poses
have a deterministic election; actual user movement takes priority. Omitted/stale peer
records retire their claim. No reward/character/card identity or continuation command is
introduced on the mod transport.

## Validation

Integration checks and final assertion counts are recorded below after the combined run.
Hardware acceptance remains open: confirm chest rewards in Campaign and Guildmaster,
locally and as the remote controlling player; verify original reward artwork, readable
Continue, shared badge/pose/resize, disabled observer input, sequential identical rewards
and successful continuation into the next action. Include late observers and flat peers.

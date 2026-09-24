# Completed story window retained after quest rewards — ModBuild 555

## Hardware evidence

The new local `LogOutput.log` identifies build 554 / `f2e9b1124`; remote logs
remain historical build 500. No matching new screenshot was supplied. This run
provides direct evidence for the previously reported inert final story window:

- Local log 7535–7540: native story clicks followed by `IsOpen=false`, while the
  mod reports that its sticky presentation is still drawing the dialog.
- Player log 18282–18285: StoryBox loses focus and unregisters, without a later
  registration of another story before shutdown.
- Local log 7665–7674: the campaign reward Continue succeeds and its window is
  released. Player log 18628–18632: retirement wait finished, result processing
  finished, actions resumed and the save succeeded.
- Further story clicks at 7734, 7759, 7762 and 7955–7956 cannot progress anything:
  the native story button is already disabled (7738, 8185). The retained window
  only releases at module shutdown (8217), after 39.1 seconds of visibility.
- The Player.log NullReferenceException at 19430 occurs after OnApplicationQuit
  and Bootstrap teardown, not during this dialog completion.

The game completed the story and rewards. The VR presentation made a completed
dialog appear to be an unanswered blocking window. This is a presentation lifetime
defect, not evidence of a failed reward callback or lost save.

## Cause and correction

`MapRoomParallel` lets destinations such as merchant and temple windows remain
side by side despite native sibling-hiding. It also marked story windows sticky.
Their identity was absent from `ClassifyMandatoryDecision`, so final native Hide
did not spend stickiness. `ReassertStickyVisible` repeatedly restored their alpha
and input area, while the native final Skip had already disabled its button.

Build 555 identifies the exact windows serialized by both MapStoryController and
StoryController. Their normal native close now ends presentation retention, even
if a stale poll entry still exists. Poll admission, conversion and visibility
reassertion cannot resurrect a completed story. The ordinary float cleanup owns
the frame, input surface and arc-seat release; no gameplay Hide, Skip or callback
is synthesized. The original full-window click remains the continuation action.

This is shared by all users of those narrative controllers, including scenario
endings, map messages, prosperity dialogue and quest introductions; it is not
keyed to a scenario name. Live pages and same-instance synchronous successor
messages stay open. Permanent character/quest windows, parallel destinations and
composed loadout hosts are not classified as stories merely because they contain
one. Native mandatory story continuation is also protected from generic close.

## Multiplayer follow-up

The user explicitly requires post-scenario dialogues to remain fully shared. The
source audit found two additional defects beyond the local retained-window bug:

- `MapStoryController.OnFinishShow` can synchronously open the next queued message.
  The legacy frame poll then sees only its new content key and never publishes the
  previous message's completion. The scenario StoryController has the same queue edge. A slower participant can remain on that predecessor.
- Reward pose identity previously recognized scenario chest/goal-chest messages only.
  Native campaign/Guildmaster post-quest rewards had no shared opening identity;
  campaign Continue is a local callback, not the scenario reward network action.

The correction records native opening/completion edges. Additive TLVs83/84 retain
sender-scoped openings, public native run/content provenance, participants, page and
completion. Rotating bounded snapshots preserve predecessor completion across packet
loss and long native reveal sequences. Repeated identical content and a reconnect
must not reuse a previous opening's completion. Legacy story poses remain on19/21;
reward presentation reuses the original shared reward pose path73/74. Pose, grip
and room-making ownership are tied to the opening from the same presence snapshot,
so a prior identical message cannot move a newly opened window. A genuinely fresh
story float rearms its common spawn seat; live/composed/manual windows keep their pose. The native
controllers still perform continuation, unlocks, reward processing and saving.

Native scenario reward authority and personal decisions must not be replaced by a
second mod gameplay action. The new post-quest continuation path waits for the
original reveal and eligible Continue; it does not skip native introduction stages.
Native reward introductions reuse their original reward host, including Guildmaster;
their forward/backward pages synchronize with explicit revision ordering. Local page
changes are sampled before applying peer state, avoiding a backwards-click bounce.
Accepted continuation stays single-use and retries only if a failed call left that
exact native opening open. Informational hint scopes survive asynchronous successors.
Guildmaster adventure rewards have mandatory identity, so a bare X cannot bypass
unlock videos and the native completion callback.

Observation patches isolate optional metadata failures from native network dispatch.
Completion diagnostics are one normal-level event per successful native story;
page details remain Debug. No remote hardware evidence exists for this run.

## Validation

The strict Release build completed with zero errors and zero warnings. The EN/DE
documentation check and workflow lint passed. No Unity bundle or player-facing
settings changed.

Focused production suites pass: story completion 325 assertions / 11 negative
controls; opening lifecycle 293 / 10; post-quest rewards 395 / 24; reward poses
110 / 8; shared reflow 84 / 7; hint ownership 48 / 8. These exercise native close
admission, queued predecessors, recipient slicing, fresh opening identity,
exactly-once native continuation, intro pagination, and stale pose/grip rejection.

The final complete `refactor-guard.sh check --summary` passed every source and
production runtime gate, including 254,965 wire assertions. Patch registration
contains 146 classes / 222 methods. No config keys, patch names or log tokens were
removed from the guard's baseline. The expected exit status is 1 for compiled
differences against `080c505e9`: 0 order-only moves, 112 changed types and 115
added/removed entries. That comparison includes intervening builds, not only 555.

## Hardware check

Use the build 554 scenario-win cheat to reach post-quest results in the new build.
Advance the final story page: its frame must leave, rewards must remain usable,
and the map must be accessible. Repeat with another quest/prosperity dialogue,
then a quest introduction that proceeds to loadout. In multiplayer, verify that
both clients dismiss the finished story and continue their original shared flow.
Test alternating which player advances pages, fast queued messages, moving the
shared window, the reward reveal/Continue and a prosperity follow-up. Repeat after
reconnecting and with delayed loading; previously completed dialogue must not close
a later new opening. Verify both Campaign and Guildmaster post-quest rewards.

Automated lifecycle checks do not establish headset animation or network delivery;
they protect the exact retained-window failure path shown by these logs.

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

The existing RemoteMapStory FINISHED/linger path already handles a null MapBox
after release and retains the sampled content key for packet-loss recovery. Its
protocol and native authority are unchanged. No remote hardware evidence exists
for this run.

## Hardware check

Use the build 554 scenario-win cheat to reach post-quest results in the new build.
Advance the final story page: its frame must leave, rewards must remain usable,
and the map must be accessible. Repeat with another quest/prosperity dialogue,
then a quest introduction that proceeds to loadout. In multiplayer, verify that
both clients dismiss the finished story and continue their original shared flow.

Automated lifecycle checks do not establish headset animation or network delivery;
they protect the exact retained-window failure path shown by these logs.

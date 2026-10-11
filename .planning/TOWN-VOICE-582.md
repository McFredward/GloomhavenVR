# Independent town resident speech — ModBuild 582 candidate

## Evidence

The newest main-checkout `Player.log` and `LogOutput.log` both banner ModBuild
580. They cannot confirm or refute the gaze-edge greeting added in ModBuild 581.
There is no per-cue speech trace in those Build 580 logs. The reported delay is
therefore hardware evidence of the old build, while the additional cross-NPC
block below is established from the current source, not inferred from a log.

## Source cause and change

The face author's ModBuild 581 path computes `lookingAtVisitor` from the
merchant's actual face target at the 2.4 m attention boundary. Its greeting is
independent of both native shop entry and the delayed coin-hand body pose.
However, `TownServiceVoiceSchedule.Sample` then refused to start **any** queued
resident line while a different resident had a cue, and imposed one shared
post-line delay. `TownServiceVoice.Observe` reused a single spatial `AudioSource`
for all three residents. A priestess prayer or enchantress cast could thus hold
the merchant's gaze greeting even while different players visited separate
stands. With the user moving closer during the wait, speech appeared to start
only at native shop/card range.

Each resident now has its own cue gap and spatial playback source. A merchant
greeting starts when that resident's own queue is ready even if another NPC is
speaking; simultaneous cues retain independent head positions, ages, and
listener claims. Native narration and story commitment still silence all
resident audio. One resident still has only one cue and its 45-second greeting
cooldown. The original authored cue/generation stream remains unchanged, so
different peers replay each NPC's same line and mouth timing.

The merchant source's existing linear rolloff spans 0.45–7.0 m at map scale.
At the 2.4 m gaze boundary its geometric gain is approximately 70% of source
gain, so there is no hard audio cutoff at the native shop radius. Hardware must
still judge perceived loudness; code tests cannot measure headset acoustics.

## Verification

`scripts/check-town-voice.py` passes 18 portable schedule assertions, 3,890
Unity 2021.3.5f1 runtime assertions, and all 20 negative controls. These
exercise concurrent merchant/priestess cues, simultaneous spatial sources and
listener claims, gaze-range gain, story silence, and cue continuation on
face-author handover. The strict Release build passes with zero warnings and
errors.
The 580 log does not prove that the 582 candidate sounds right in the headset.

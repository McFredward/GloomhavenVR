# Full trailer edit and original controlboard references

The maintainer approved production of the complete 65-second trailer after the
revised film-world review. The latest requirement adds a cinematic player mask
placing cards on the actual controlboard, followed by the resulting hero action.
English narration introduces a familiar world experienced differently and
names GloomhavenVR. The root producer supplies the final exact edit JSON and
licensed source metadata. All private media and review evidence belong under
the main checkout's gitignored `.planning/debug/trailer/`.

## Source controlboard and card print

The native reference video is `reference/gameplay/download/video.mp4`, from
`https://www.youtube.com/watch?v=tgJAk6Z62b4`. Timestamped source frames and their
index live under `full-production/controlboard/`. The visible controlboard is
**Steel**, verified against `VRCardFactory.SteelTrayPath` and the editor's
`BuildBoard` definitions. Its original model and texture set are:

- `unity/GloomhavenVR.Assets/Assets/Bundle/Table/PlayTray_9capjqp6.fbx`
- `PlayTray_9capjqp6.prefab`, `PlayTray_9capjqp6_albedo.png`,
  `PlayTray_9capjqp6_normal.png`, `PlayTray_9capjqp6_mrs.png` beside that model.

The actual layout has two central vertical card wells, two rest-symbol seats
on the left and three button seats on the right. The model's original Slot1,
Slot2 and button anchor positions are recorded in the private source-render
manifest. These are not scenario-floor hexes or generic wooden card slots.
`source-render/steel-layout.png` is a clean 1920p alpha reference rendered from
the unchanged source geometry/UVs with adapted lighting. The accompanying
three-quarter reference shows the original physical recess depth. No dynamic
UI labels or false cards were drawn on those references.

Native footage at 403 seconds shows the empty controlboard. At 418 seconds,
the original Brute card fan and arcane hand are visible. At 422 seconds,
Overwhelming Assault occupies the true left card well during placement. At
541 seconds, both complete original fronts are visible: Overwhelming Assault
left and Trample right, with Brute selected at initiative 61. The selected
halves are Overwhelming Assault's top and Trample's bottom. The source captions
also identify those two choices before the Brute's turn. The later clip around
641–645 seconds belongs to subsequent play; do not silently use it as proof of
that particular Brute-card result.
`action-watch/` shows both complete fronts at 440 seconds, Assault's active
top and the native Confirm Targets cap at 564 seconds, and the original Brute
melee outcome with Damage 6 at 568 seconds. `card-crops/` contains direct
440/541-second video crops enlarged for private identity reference. Every
complete native title, print and border is retained; no fonts/text were rebuilt.
Trample's bottom visibly reads Move 4, Jump and Attack 2 against enemies moved
through. Pierce 2 belongs to its upper Attack 3, not that bottom action. These
references do not establish a legal route or invent affected targets.

Original Brute card UI components are exported read-only under
`native-card-textures/`, including `AC_Brute_Background-Sprite.png` and the
original top/bottom sprites. Their source banks are
`character_gui_assets_ac_brute_{background,top,bottom}.bundle` in the game's
StreamingAssets. The private manifest retains source hashes and Unity path IDs.
These images contain the original artwork/frame, **not dynamically composed
names, initiative, action text or icons**. Use the native footage's complete
fronts or an actual native widget capture when readable card text is needed.
Never describe those empty component textures as complete ability cards.

## Optional audio fades and music ducking

`scripts/trailer/make_review.py` retains its explicit 48 kHz sample timeline,
continuous lower-right fan-mod label, finite movie/audio duration, source hashes,
atomic completed-movie publication and owned intermediate cleanup. Existing
edit JSON remains compatible. The only additions are bounded audio controls.

An optional top-level `audio_post_gain_db` applies a final mastering trim after
normalization. It defaults to0dB, preserving existing edits, and accepts finite
values from-24 through0dB. The full65-second mix uses-1dB after its measured
normalizer output of-13LUFS, targeting approximately-14LUFS while leaving AAC
peak headroom. This trims the complete mix without changing voice/music balance,
ducking, cue placement or source audio. Verify the actual final encoded movie;
the requested normalizer setting alone is not a loudness measurement.

Each audio event may set `fade_in` and `fade_out` in seconds. Defaults remain
0.02 and 0.05 seconds. Zero disables a fade. Values must be finite and
non-negative; fades clamp to the actual placed excerpt length, including an
excerpt truncated at the edit's end. This permits longer music transitions
without changing voice/SFX behavior by default.

Music ducking is optional. Set top-level `audio_ducking` and mark audio events
with `role: "music"` or `role: "voice"`. Other/unmarked events retain their
ordinary mix behavior. `gain` is the retained music fraction during a voice
event, `attack` is the ramp time before that event and `release` is the ramp
back afterward. Overlapping voice intervals retain the strongest reduction.
Voice and sound-effect samples are not themselves ducked. Without that
top-level object, roles have no effect and existing mixes remain unchanged.

```json
{
  "audio_ducking": {"gain": 0.30, "attack": 0.12, "release": 0.35},
  "audio": [
    {"source": "/private/score.mp3", "duration": 65, "at": 0,
     "role": "music", "volume": 0.6, "fade_in": 1.2, "fade_out": 2.5},
    {"source": "/private/narration.flac", "start": 2, "duration": 2.8,
     "at": 10, "role": "voice", "volume": 1}
  ]
}
```

This is a deterministic editorial envelope over declared voice intervals,
not a speech detector. Keep each voice event's duration close to its actual
spoken excerpt. Final loudness normalization remains unchanged; measure the
actual completed master rather than assuming the requested target was met.

## Completed first cut

The private `review/10-full-trailer-v1.mp4` is the first complete 65-second cut.
Its exact edit specification is `full-production/full-edit-v1.json`. Local
frame-by-frame correction uses the original full card fronts with tracked
perspective, finger occlusion, low-frequency shading and motion blur. It covers
the macro placements, confirmation insert and later action/companion board
views. Generated source clips remain untouched. The macro framing excludes the
superseded tavern backdrop; all outer wides use the native-referenced night
forest and planked table.

The full export passes decoding, 1560 frames at24fps, stereo48kHz and continuous
unofficial-label checks on every frame. Its measured AAC mix is -14.0 LUFS and
-1.9 dBTP. Final speech transcription matches all33 script words. The submitted
speech-QC source and final encoder use byte-identical PCM, recorded by hash.
The integrator's watch review covers 13 scene-change plus48 dense final cue
frames and full-resolution source contacts. It does not establish a recorded
native turn, identical generated geometry or final maintainer approval.

## Focused validation and storage

Owned synthetic stereo sine fixtures under `full-production/edit-qc/` exercise
real FFmpeg decoding/normalization and measure the exact pre-normalization sample
timeline. Checks establish half-fade amplitude, zero-fade
behavior, a 0.25 music gain during speech, unity restoration afterward,
unchanged voice amplitude, and an exact 65-second output with a late excerpt
fading at the edit endpoint. Negative controls reject a negative fade,
non-finite fade and duck gain above unity. Fixture scripts, source WAVs, output
WAVs, hashes, numeric results and logs are retained as evidence.

Those fade/ducking fixtures remain inherited evidence for unchanged controls.
The mastering-trim addition is validated by the complete65-second real render,
measured final loudness/peak, in-memory syntax compilation and bounded-value
rejection checks. No new complete runtime gate is claimed.

No gameplay code, model geometry or read-only game data changes. Validation is
focused production-tool evidence, not a new complete runtime gate or headset
result. Reap processes before calling `cleanup_completed_run`; remove only
owned regenerable Python/encode caches afterward. Original gameplay, exported
source components, card/reference frames and current deliverables remain.

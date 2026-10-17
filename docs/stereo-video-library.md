# Native stereo movie library

The maintainer approved the actual Build687 GloomhavenVR intro in the headset on
2026-10-17 and requested the remaining native films using the same picture and
depth profile. The intro's stronger wordmark is a separate root-owned revision.
The gameplay trailer was explicitly excluded later in the same review because
its flat gameplay elements may need a different treatment. It receives no new
companion, stereo file or review trigger.

The exact catalogue lives in `scripts/stereo-video/library-manifest.json`:
11 hero unlocks, seven ambient films, the already replaced campaign intro, and
the original embedded Twin Sails startup mark. Preserve the native misspelling
`Amibient_07_Dungeon_01`. All external films are original 1920x1080 at 60 fps;
the studio mark is original 1920x1080 at 30 fps. The output has two full 1920x1080
views side by side, left first. Never resample the studio clip to 60 fps.

## Source authority

`library_catalogue.py scan` reads the game references without modifying them. It
inventories the 19 native external MOVs and every VideoClip in all 28 serialized
level/assets files. Windows uses `StreamingAssets/Movies`; the 30 fps MOV tree is
the `GameCoreXboxOne` branch, not the Windows branch. The alternate 30 fps copies
have different endings and are not conversion inputs.

There are exactly two embedded VideoClips in these references:

| Source | Native resource | Decoded clock | Treatment |
| --- | --- | --- | --- |
| `sharedassets12.assets`, path8, `TWINSAILS_GFX_ANIMATION_LOGO_2K_30FPS_H264_best` | `sharedassets12.resource`, offset0,758298bytes | 240frames at 30 fps,8s | Byte-identical original companion and versioned stereo MOV |
| `sharedassets1.assets`, path1696, `gameplay_trailer` | `sharedassets1.resource`, offset0,220149969bytes | 6500actualframes at 30 fps,216.666667s | Explicitly excluded; native source remains unchanged |

Unity declares6501 frames for the trailer, whereas the extracted native media
contains6500. This disagreement is recorded, not corrected by inserting a frame.
The trailer extraction and census are private developer evidence only.

The serialized startup `IntroPlayer` waits4seconds before hiding its Video
object and beginning a separate native sprite-logo sequence. The complete clip
is8seconds. Conversion does not change that native continuation or wait; a
standalone review can show the complete clip. The source startup camera is the
UI-only orthographic camera27 in `level12`, with the VideoPlayer58 drawing to its
near plane and no RenderTexture. VideoPlayer58 serializes `playOnAwake=false`,
`looping=false` and `targetCameraAlpha=1`, so serialized autoplay does not precede
`IntroPlayer.ShowVideo` and its explicit `Play` call.

The ambient `MainVideoProvider` is level1 component11392, bound to VideoPlayer10593.
It serializes disabled, `playOnAwake=false`, `looping=true`, CameraFarPlane,
`targetCameraAlpha=1` and no RenderTexture. `BackgroundView` supplies the native
ambient URL before explicit playback. Its retained serialized initial trailer
clip is not an added conversion subject and does not revoke the exclusion.

The retained gameplay trailer is not scheduled by the supplied menu scenes:
`MainMenu`/`MainMenu_gamepad` (`level2`/`level3`) serialize
`PromotionVideoHandler.showAtStart=false`, although `timesVideoShows` is3.
Consequently the class's stored `PromotionVideo` counter does not enable its
first-three-launch branch. Neither supplied menu has a serialized `PlayVideo`
UnityEvent, and the decompiled game has no additional caller. This is source and
asset evidence, not an observed hardware startup event.

## Offline conversion

Use the existing `intro-requirements.lock` with CPU Torch2.1.1, Python3.11, and no
xformers. The catalogue additionally uses UnityPy1.25.4. The model stays an
external pinned input: Video Depth Anything Small, code revision
`4f5ae23172ba60fd7bc11ef671cca678842c7072`, checkpoint revision
`256875362cff76724b920335dfb4b29dd611f66e`, checkpoint SHA256
`13379300b739e659f076a59d52e9801bd8d38c541a7e71f73bbca4dcfb013609`.
No paid service, model or inference runtime is introduced into the game.

`library_depth.py` uses the unchanged CPU FP32 profile: model input392, native
32-frame temporal windows with10-frame overlap, every original frame, saved
640x360 FP16 depth, and one2nd/98th-percentile normalization for each original
shot. The original near-black separators divide independent inference shots.
Atomic hash-bound receipts permit a stopped batch to resume without overwriting
completed depth. An invalid or orphan result is refused for explicit review.

The initial32-frame benchmark performs two complete model windows. Thread counts
4/8/6 took48.70/49.76/47.61seconds, respectively, with maximum numeric differences
below 0.000020. Six threads preserve the accepted intro's setting. The first389
ambient source frames completed in401.187seconds. These are local CPU timings,
not headset decoder or renderer timings.

`library_stereo.py` consumes only validated receipts. Original native YUV is the
left picture before final H264 compression. The right picture uses the accepted
horizontal forward reprojection, foreground depth test, luminance-guided depth
upsampling, disparity[-4,+12]pixels and64pixel outer-edge feather. Newly exposed
pixels take adjoining farther original background; there is no generative
inpainting, replacement artwork or invented scene content.

Native hero opening emblems retain the intro's screen-plane title treatment.
The final hero name/banner and class symbol keep a protected screen-plane region
on their observed native left or right side; character geometry outside that
printed panel remains depth-reprojected. Original cinematic bars are preserved.
The original studio mark uses a uniform+8pixel positive-disparity plane so its
lettering remains rigid. Source black fades taper depth using the same luminance
rule as687. Ambient first/last original black frames have zero disparity, closing
their native loop without a new stereo jump.

Each full-SBS movie retains its exact original frame count/fps, ending and all
AAC packet payloads/timestamps/durations; ambient sources remain silent. Encoding
uses the accepted explicit BT709 limited-range YUV420, accurate full-chroma right
conversion, H264 High5.1, CRF17 and17Mbps/34Mbps VBV. The 100 MiB per-file limit is
checked after rendering; quality is not weakened merely to fit that limit.
Every render retains exact source snapshots, commands, shot geometry and cue
pictures beside its private receipt.

Example (paths intentionally supplied by the developer):

```sh
python scripts/stereo-video/library_depth.py \
  --data-root "$game_data" --embedded "$extracted_clips" \
  --model-root "$pinned_model" --out "$depth_output" --threads 6
python scripts/stereo-video/library_stereo.py \
  --movie Ambient/Ambient_02_Cave_01.mov \
  --data-root "$game_data" --embedded "$extracted_clips" \
  --depths "$depth_output" --out "$fresh_render_directory"
```

The example is developer-only; the normal installer consumes finished separate
movie Content beside the DLL.

## Verification and limits

`library_verify.py` independently decodes every output frame timestamp and left
picture, compares the complete original AAC packet sequence, validates depth
pins/shapes/hashes, and checks start/middle/end encoded right witnesses for every
shot. Both picture metrics use declared native YUV420, with the same 34 dB minimum
as the approved intro. Strong depth witnesses also reject a duplicated left eye.
The verifier validates protected print regions and both native ambient loop
endpoints. `library_review.py` additionally extracts three actual encoded PNG
cue pairs with explicit original frame addresses and checked selected PTS, using
`-fps_mode passthrough`. Ambient cues cover all three native views; hero cues
include the final protected name/symbol panel. Manual inspection follows the
automatic gate, with its own hash-bound record in per-film provenance. Decoder children are killed/reaped before the shared completion
cleanup API runs, including failures and interrupts.

`tests/stereo-video-library/check_library.py` has18 physical/inventory checks and
11 refusal controls: known near/far directions, unchanged vertical addresses,
real disocclusion, duplicated/reversed eye differences, actual YUV chroma crop,
protected print regions, disparity limits, black fades, native30fps timing,
trailer/root-owned refusal, shot gaps, lost ending, wrong timestamps, half-SBS,
wrong SAR, duration and color metadata. Its first geometry attempt used an
ill-posed far-object witness whose supposedly background plane was nearer than
the object. Correcting that fixture to a farther background passes; the failed
attempt remains retained and no production geometry was relaxed.

The studio clip's complete encoded verification passes240 left frames and
240timestamps,375 identical AAC packets and three right witnesses. Minimum PSNR
is56.16dB left and64.87dB sampled right. The first cave ambience also passes1125 left frames, all native60fps timestamps,
three depth shots and21 right witnesses, with minima40.74dB left/42.64dB right
and zero-disparity original loop endpoints. Both completed subjects have three
actual decoded cue pairs visually inspected. Per-film provenance states completed media
results as they become available; this paragraph is not a completed-library claim.

The movable VR surface displays a recorded fixed stereo pair. Moving the window
or head changes the view of the surface, not the original camera positions
inside the film. No head-pose view synthesis or six-degree-of-freedom scene
reconstruction is introduced. Automated media/geometry evidence does not prove
headset fusion, comfort, motion, decoder throughput or every native spawn path.
Those remain subjects of the requested in-scenario review controls.

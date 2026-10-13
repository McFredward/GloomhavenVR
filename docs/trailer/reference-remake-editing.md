# Reference trailer editing

The active trailer uses the official Gameplay Overview film as its timeline
master. Selected existing cinematic shots receive mask/hand additions from
video-edit models. The maintainer will supply real VR footage for the Flat
explanation shots after the complete cinematic remake has been self-reviewed. Keep
the original English narration and mix except the requested opening name patch:
the first spoken Gloomhaven becomes GloomhavenVR. The original title reveal at
7.000–9.050s receives the VR suffix from the native mod logo.

## Active interaction direction

The maintainer's later 2026-10-13 feedback permits meaningful changes to a
selected shot's interior action and camera. Giant hands may physically guide or
move a living character from above. Keep the original first/last images, the
window's frame count, soundtrack clock and approximate narrative sequence;
matching a single endpoint does not prove a smooth join in its neighborhood.
This supersedes the earlier requirement to lock every original actor/camera
pixel throughout an edited shot. Identity, anatomy, environment and lighting
still need visual review.

Vary one-player and two-player scenes across all three native masks: Ironwatch
(Mask_0), Runeveil (Mask_1) and Grimhorn (Mask_2). Hands need a purposeful reach,
credible contact, living weight transfer, release and withdrawal, with relaxed
finger/wrist articulation and smooth acceleration. A fleeting background mask
is insufficient for the new interaction pilot. Avoid repeating two masks in
every edit. Native hand styles can be paired deliberately; the mod selects mask
and hand style independently.

The new I01 pilot uses the original cloaked-client scene from 139.950–145.700s:
frames 8397–8741 inclusive, exactly 345 frames at 60fps / 5.75s. Ironwatch's native
right metal hand guides the outer left shoulder (screen-right) of the rocky-faced
mercenary left of the foreground client, then retracts above. Describe that
target visually rather than misidentifying it as Brute. The original animated
world-map transition after 145.700s stays untouched. Source frames and native
identity references live under `ai-edits-v3/`.

One [Seedance 2.5 editing request](https://fal.ai/models/bytedance/seedance-2.5/reference-to-video/api)
was generated at 720p with the existing video as its sole master, exact source
endpoint images and native Ironwatch/metal-hand reference. This trial reserves
USD3.80; [current provider pricing](https://fal.ai/models/bytedance/seedance-2.5/reference-to-video)
bills both input and output video seconds. The nominal 1280×720 token estimate
for 5.75s input plus 5.75s output is USD3.19 before duration differences. That
trial brought conservative reservations to USD37.85. The invoice
is not reconciled; 720p is a direction trial, not a final-resolution approval.
The raw137-frame/24fps result lasts5.708333s. Root inspected12 dense cue frames;
the worker inspected19. Both rejected it: the native metal hand, shoulder
contact and sidestep are absent, and Ironwatch sinks onto the warrior face. It
is not inserted into the master. `ai-edits-v3/REVIEW.html` presents the raw
result, original scene and completed intro as historical comparison. The latest
user instruction explicitly authorizes completion and self-review of the whole
trailer without per-shot approval pauses. All Flat gameplay remains untouched
until genuine VR recordings are supplied.

A new [Kling O3 Pro reference-video request](https://fal.ai/models/fal-ai/kling-video/o3/pro/video-to-video/reference/api)
uses the original clip, a visually reviewed unified middle-scene guide, native
Ironwatch/metal-hand identity and source endpoint images. Its interior camera
and staging may change to create physical depth, contact, correct occlusion and
matching torch light. The six-second trial reserves USD1.35; total reservations
are USD39.20 against the authorized USD40 cap. A proposed USD45 ceiling is
pending and cannot be spent before explicit authorization. A good guide is not
a video approval. Root review of26 sampled video frames rejected this output: the new
hand appears only near the end, misses the intended shoulder guidance, and the
added props remain at the source endpoint. It is preserved but excluded.

The next trial uses [Kling O3 Pro start/end images](https://fal.ai/models/fal-ai/kling-video/o3/pro/image-to-video/api)
for two three-second phases sharing one unified contact guide. The entry begins
at the source black frame and reaches the guide; the exit begins at that guide
and should physically withdraw the mask/hand to the exact original party ending.
Each reserves USD0.40. Entry review covers13 dense actual video frames and
supported the paired withdrawal trial. Exit review covers13 cue frames: credible
shoulder contact, slight living upper-body shift and physical upward withdrawal
to the original party ending. A footstep is hidden by the crop and unverified.
The offline pair restores345 frames/60fps and exact source endpoint pixels.
Root review of29 normalized frames caught thin source-picture stripes copied
by overly broad black-bar protection; a versioned black-core repair is in progress.
Complete-shot and master approval remain pending. Total reservations are now
USD40.00; no further inference is authorized until the pending budget decision.
A read-only billing-event request returned403 with the configured key; no
reservation was released or actual invoice amount claimed.

The complete film now plans O01 solo Runeveil(3.000–7.000s), I01 solo Ironwatch
and A04 Grimhorn/Runeveil cooperation. A03 remains original because early native
lettering and class silhouettes leave insufficient natural interaction time.
Source frames, native references and integrated middle guides for O01/A04 are
prepared without fal calls. This preserves all three mask identities and varied
one/two-player scenes while keeping readable source typography intact.

## Earlier overhead placement diagnosis

The first A04 insertion puts Runeveil too low beside the Tinkerer. The maintainer
requires the character-height camera to see gigantic player masks high above
the living world, looking down like gods. Hands can enter from above for physical
interaction. Source heroes carry out their own attacks. Masks stay observers;
neither mask nor hand becomes a fighter or a spellcaster.

Use the actual mod assets across the film. A two-player shot uses distinct
Grimhorn and Runeveil masks, their own native hand styles and attention directed
at the same world below. Upper-frame cropping is preferable to lowering a mask
to hero height. The earlier diagnostic preserved source actors and camera
throughout. The active direction permits interior camera/action changes while
source endpoint images and timing are retained. Additional original table, Steel
controlboard and card assets belong
in player-space/table views; printed rules still come from native captures.

The completed placement diagnosis lives under `ai-edits-v2/`. It reserves USD2.80:
USD1.35 for the completed two-player edit, USD0.10 for the opening narrator patch,
and USD1.35 retained for a request rejected by provider validation. The rejected
request produced no video. The explicit corrected request fits the provider's
2,500-character prompt limit; the shared submission helper now checks that limit
before reserving budget or uploading inputs. The subsequent feedback above
authorizes the broader interaction pilot instead of extending this placement.

All media, prompts, inference receipts and review movies stay in the main
checkout's gitignored `.planning/debug/trailer/remake-v1/ai-edits-v3/`.
Earlier insertion/placement diagnoses remain under `ai-edits-v1/` and `ai-edits-v2/`.
The source download and manual-caption analysis remain under
`.planning/debug/trailer/reference/vr-remake/`. The previous65-second CGI
film is retained as superseded work.

## First paid candidate

A04 covers source frames13032 through13426, inclusive:217.200–223.783333s,
395frames at60fps. The historical Runeveil/Arcane placement was in the dark forest
beside the original Tinkerer; that placement requires the correction above.
Preserve the native mask shape, violet eyes,
gold ribs and small forehead point. Cyan hand glyphs belong on the backs;
palms remain plain leather. Players have floating masks and separated hands,
without invented arms or bodies.

The source sequence includes Tinkerer, Cragheart and an Earth Demon. Their
original actions and foreground weapon must survive the edit. The added
presence observes; it does not cast either hero's attack.

The first request used [Kling O3 Pro video editing](https://fal.ai/models/fal-ai/kling-video/o3/pro/video-to-video/edit/api),
with native identity elements and a source-based placement image. The output
is1920×1080,161frames at24fps,6.708333s. This is longer than the original
window and does not establish pixel fidelity or creative approval. Local
registration, moving mattes and foreground exclusions restore the source
timing and retain the original outside the additions. Review the short mask
presence and the whip-pan exit before applying the approach to other shots.

The maintainer authorized proceeding sparingly in response to a proposed
additionalUSD10 round. The hard total ceiling is nowUSD40, including
USD29.90 of earlier conservative reservations. A04 reservesUSD1.35. The
provider invoice has not been reconciled; reservations are not invoiced costs.
Total conservative reservations before the new interactive trial were USD34.05.

The historical first inserted review remains in `ai-edits-v1/REVIEW.html` and
`ai-edits-v1/reference-remake-ai-pilot-v1.mp4`. The presence is visible briefly
at03:37.200; after local shot frame78, the remaining original shot is source
only. The isolated corrected clip is under `A04/native-composite/v2/`.
The maintainer's placement feedback supersedes this candidate's direction.

The original14810-frame full film decodes, and compressed original audio packet
hashes are identical. Root inspection covers13 focused clip frames,31 dense
transition witnesses and14 exported full-film cue frames. The actual local
browser page loads all three videos and its seek control reaches the edited
shot. These checks support an initial direction review, not creative approval.

## Offline assembly

`scripts/trailer/composite_reference_gods.py` isolates Grimhorn, Runeveil and
their native hand styles from the new edit, raises the props above the source
characters, and registers them to the original camera. Traced silhouettes and
source foreground holdouts keep generated actors and scenery out of the master.
The local correction uses `presence-composite/v4/`; earlier exports
remain as evidence. This source shot offers only a brief two-player reveal with
strong upper-frame cropping, followed by the original hero actions. Planar
registration, interpolated poses and manual silhouettes approximate depth; the
technical checks do not establish imperceptible joins or creative approval.
Dense review of v3 also identified doubled mouths/chins from pose crossfading;
the delivery must align moving poses before blending them.

`scripts/trailer/patch_reference_intro.py` adds the exact native VR suffix to
the original title, keeping its lettering, background and fireburst. The v3
title uses one validated stationary anchor and a smooth reveal to remove the
per-frame registration jitter found in v2. The prepared narration master keeps
the original spoken Gloomhaven, appends the authorized narrator clone's “V R”,
and shifts the original “recruit” by 0.525s. Local separation retains the music
under the bounded 6.225–7.575s edit. Saved Float32 PCM samples outside that window
are unchanged; the browser-compatible final AAC encode is lossy throughout.

`scripts/trailer/composite_reference_presence.py` is an A04-specific offline
OpenCV utility. It registers the added layer, restores source foregrounds and
exports lossless60fps plates/mattes. It is not a general object-segmentation
system; depth and whip motion blur are approximations. The short presence has
restrained independent movement. Existing completed plates are never overwritten.

`scripts/trailer/assemble_reference_remake.py` normalizes replacement layers
to a fixed1080p60 timeline and accepts moving grayscale mattes. White replaces
the source, black retains it. For already registered60fps plates, use
`timing: "trim"`. Generic `retime` is available for reviewed replacements whose
complete input span must fit a specified window; it does not validate action
or camera fidelity.

```json
{
  "source": "/absolute/original.mp4",
  "output": "/absolute/new-review.mp4",
  "preset": "fast",
  "footer_height": 0,
  "clips": [{
    "id": "A04",
    "start_seconds": 217.2,
    "end_seconds": 223.783333,
    "path": "/absolute/aligned-presence.mkv",
    "mask_path": "/absolute/moving-matte.mkv",
    "timing": "trim"
  }]
}
```

```bash
python3 scripts/trailer/assemble_reference_remake.py manifest.json --plan
python3 scripts/trailer/assemble_reference_remake.py manifest.json --render --threads 4
```

The source has14810 video frames,246.833333s of video and246.861s of audio.
Keep all original frame addresses and the longer audio tail. Default assembly
copies the original Opus stream and verifies identical compressed audio packet
hashes; it never maps model audio. `--aac-audio` provides an explicitly logged
AAC320k compatibility transcode of the same original mix.

Local typography burns `UNOFFICIAL · FAN-MADE MOD` continuously into the
existing bottom black bar. Optional intro/end graphics identify the fan mod
without retaining the original2021 release announcement. Exact printed text
and the original mod logo come from local assets, never video-model lettering.

Outputs are versioned and never overwritten. Assembly retains input hashes,
commands, graph, complete decode/timing/audio QC and interruption evidence
beside the movie. Reap encoders and run the central completion cleanup API;
preserve original sources, captures, manifests and current deliverables.
Technical checks do not establish invisible AI seams or a correct headset view.

## Private media organization and storage

The trailer root `START-HERE.html` links current work, completed intro, historical
full-film diagnoses, recording instructions and every preserved AI raw video.
Do not rename active folders or break absolute provenance paths. Fourteen
SHA-identical archive copies were atomically replaced with hardlinks to their
immutable provider originals; every pathname, byte and modification timestamp
was preserved. Unique allocated inode blocks fell by 142,843,904 bytes
(136.23MiB). Native `.blend` sources, borrowed environments, source captures,
all AI-generated videos and current deliverables remain intact.

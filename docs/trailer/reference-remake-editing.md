# Reference trailer editing

The active trailer uses the official Gameplay Overview film as its timeline
master. Selected existing cinematic shots receive mask/hand additions from
video-edit models. The maintainer will supply real VR footage for the Flat
explanation shots after the first AI insertion passes direction review. Keep
the original English narration and mix for this first visual pilot; small
GloomhavenVR name patches remain a later step.

All media, prompts, inference receipts and review movies stay in the main
checkout's gitignored `.planning/debug/trailer/remake-v1/ai-edits-v1/`.
The source download and manual-caption analysis remain under
`.planning/debug/trailer/reference/vr-remake/`. The previous65-second CGI
film is retained as superseded work.

## First paid candidate

A04 covers source frames13032 through13426, inclusive:217.200–223.783333s,
395frames at60fps. Runeveil and the Arcane hands appear in the dark forest
beside the original Tinkerer. Preserve the native mask shape, violet eyes,
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
No further editing request is needed before the first direction review.

The first inserted review is ready in `ai-edits-v1/REVIEW.html` and
`ai-edits-v1/reference-remake-ai-pilot-v1.mp4`. The presence is visible briefly
at03:37.200; after local shot frame78, the remaining original shot is source
only. The isolated corrected clip is under `A04/native-composite/v2/`.
The maintainer reviews this candidate before additional paid shots.

The original14810-frame full film decodes, and compressed original audio packet
hashes are identical. Root inspection covers13 focused clip frames,31 dense
transition witnesses and14 exported full-film cue frames. The actual local
browser page loads all three videos and its seek control reaches the edited
shot. These checks support an initial direction review, not creative approval.

## Offline assembly

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

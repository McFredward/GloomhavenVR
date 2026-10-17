# Approved GloomhavenVR intro in stereo

## Scope and media authority

The maintainer confirmed the685 chart test on2026-10-17, then explicitly asked
to remove the test film/action and convert the already replaced, currently flat
**GloomhavenVR intro** directly. The maintainer then approved Build687 in the
headset, requested slightly stronger foreground depth for the GloomhavenVR
wordmark, and authorized the remaining native films. That follow-up is described
in [the library contract](stereo-video-library.md); the gameplay trailer is
explicitly excluded.

The sole source is `Assets/Movies/CP_Intro/GH_CP_Intro.mov`, SHA256
`22fc9f48d4da6783fed379122e8c5675c4149afd9c0bc90550a9e4e22696fec5`.
It is the approved notice-free mod intro, with the VR wordmark, approved opening,
cuts, native ending and soundtrack described in [its provenance](trailer/game-intro.md).
The original game movie is read-only and is not used as conversion input.

`GH_CP_Intro.sbs-v2.mov` is a separate3840x1080,60fps H264/AAC MOV, left eye
first. Both1920x1080 views share all2931 original frame addresses and the48.85s
native completion clock. The decoded approved picture is the left view; the
right view is depth-reprojected. The original2291 AAC packet payloads, timestamps
and durations are stream-copied and independently compared. Original publisher
marks and the black ending stay at zero disparity. The two existing GloomhavenVR
wordmark appearances now use a rigid+12pixel foreground plane. No caption, actor,
logo, scene, speed, edit or soundtrack is generated or replaced.

## Offline conversion

The depth estimator is [Video Depth Anything Small](https://github.com/DepthAnything/Video-Depth-Anything),
run locally in FP32 on CPU, without xformers or paid inference. Code revision
`4f5ae23172ba60fd7bc11ef671cca678842c7072`, model revision
`256875362cff76724b920335dfb4b29dd611f66e`, and checkpoint SHA256
`13379300b739e659f076a59d52e9801bd8d38c541a7e71f73bbca4dcfb013609`
are pinned. The116,440,756-byte [official checkpoint](https://huggingface.co/depth-anything/Video-Depth-Anything-Small/resolve/256875362cff76724b920335dfb4b29dd611f66e/video_depth_anything_vits.pth)
and upstream code remain private external build inputs. Small's code/model
license is Apache-2.0; retain the upstream license with those local inputs.
No model or Python dependency is included in the game installation.

Use an isolated Python3.11 environment and
`scripts/stereo-video/intro-requirements.lock` with the official CPU wheel index
`https://download.pytorch.org/whl/cpu`. Do not install upstream's entire
requirements file: importable xformers selects a CUDA attention operation on CPU.
The actual32-frame392-input pilot took39.763s and peaked at4,750,328KiB RSS;
the preceding upstream CLI completed252-input inference but failed in its
optional imageio preview writer. That failed attempt is retained and is not a
successful end-to-end benchmark. The production tool uses direct OpenCV/ffmpeg
I/O, without that optional writer.

`intro_depth.py` checks the approved source, upstream pins and model worktree.
`intro-shots.json` covers every original frame once, splits actual hard cuts and
black separators, and retains the native spider/party dissolve in one segment.
The model's32-frame temporal window, ten-frame overlap and upstream alignment
remain unchanged. Depth is computed for every60fps frame at model input392,
stored as640x360 FP16 per shot, and uses one shot-wide normalization. This keeps
memory/disk bounded and avoids normalization changes on every frame.

`intro_stereo.py` upsamples with source-luminance guidance, then forward-projects
only horizontally. Larger inverse depth wins an overlap. The disparity convention
is `x_left - x_right`: bounds are-4px far and+12px near. Original letterboxes and
the outer picture edges stay fixed. Only newly uncovered samples are filled
from the farther adjacent source background; there is no generative inpainting.
This can stretch a narrow strip of background and cannot recover unseen detail.
The floating opening mask receives a largely planar depth estimate. These are
known reconstruction limits, not headset-confirmed defects or quality claims.

Example, with already downloaded pinned upstream inputs:

```bash
python scripts/stereo-video/intro_depth.py \
  --source src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.mov \
  --model-root /path/to/pinned/Video-Depth-Anything \
  --shots scripts/stereo-video/intro-shots.json \
  --out .planning/debug/stereo-intro/depth392
python scripts/stereo-video/intro_stereo.py \
  --source src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.mov \
  --depths .planning/debug/stereo-intro/depth392 \
  --logo-depth scripts/stereo-video/intro-logo-depth.json \
  --out .planning/debug/stereo-intro/render
```

Outputs are never silently overwritten. Receipts retain original frame ranges,
model pins, normalization, depth hashes and filled-hole measurements. The first
intermediate encoder started before receipt hardening and was deliberately
interrupted after its unconstrained bitrate projected beyond the single-file
delivery budget. A second intermediate used the hardened receipts and bounded
CRF18/15Mbps VBV, then was interrupted after independent color controls proved
OpenCV's decode matched forced BT601 exactly despite this source's BT709 metadata
(frame240: maximum15 RGB levels different from BT709,40.247dB). Neither candidate
was delivered. Logs, partial files, commands and actually loaded generator
snapshots are retained. The final encoder takes unmodified original YUV directly
for left, and explicitly BT709-decoded RGB for right. The neural depth input uses
upstream's ordinary OpenCV reader; it never supplies final picture color.

The first correctly colored full candidate (private render-v3) retained all
2931 frames and2291 original AAC packets. Its left-eye YUV comparison passed
every frame (minimum34.48dB), but a bright spell's right RGB witness at1859
measured33.78dB against the required34dB minimum. It was not delivered. A separate
lossless420 roundtrip measured41.65dB and exposed avoidable conversion rounding;
full-chroma/accurate-rounding conversion improved that oracle to45.16dB.
Render-v4 uses that conversion, CRF17 and17Mbps/34Mb VBV. A later end-of-spell
witness exposed an inconsistent comparison space: left was tested in native
YUV420, right against RGB before chroma subsampling and a second RGB reconstruction.
An exact-frame offset control selects1913 unambiguously (RGB33.80dB versus27.26dB
at1912 and13.00dB at1914); the same encoded right frame measures37.30dB in its
declared YUV420 format. Both eyes now use that native-format comparison with the
same34dB minimum. This correction is recorded separately from the encoder quality
improvement; earlier failed results are retained. Full-size per-eye pictures,
source frame addresses and audio are unchanged.

## Native playback and delivery

The native URL setter chooses the exact stereo asset before the original first
Play, only while VR world UI conversion and the actual MultiPass renderer are
available. The native VideoPlayer continues to own decoding, audio, time,
completion, errors and skip. Its existing camera lease/render target are intact.
The world-space RawImage uses the tested embedded per-eye shader and per-eye
16:9 aspect. Mono/unknown cameras use left. Closing or external destruction
releases the owned material/camera hooks. The library follow-up adds a deliberate
intro review under Advanced > Test triggers, without a persistent setting or
invoking native campaign continuation.

The recorded stereo pair follows the existing movable, yaw-only video window.
Moving or resizing that window moves or scales the whole stereo presentation.
Head position changes the view of the window, but cannot generate new scene
perspectives, positional parallax or unseen surfaces behind foreground objects.
Depth is encoded as fixed pixel disparity relative to the screen plane; window
size, viewing distance and angle affect its apparent strength. This is a stereo
film, without head-pose view synthesis or volumetric reconstruction.

Existing TLV72 carries the actual versioned selected movie key; its wire layout
is unchanged. Peers resolve the exact selected revision and never upgrade an
owner-selected2D movie locally. A renderer failure displays the same SBS file's
left half at16:9. A missing SBS asset may resolve only its approved2D companion;
missing both refuses the cosmetic decoder with owner/opening/key/reason context.
The approved2D key requires its mod-local companion as well. A native owner
missing both mod movies publishes the explicit `.native.mov` alias, which peers
resolve only to their read-only game original even if their mod movies exist.
The URL assignment retains that selected provenance for the current player;
adding media later cannot change an already published opening's identity.
These are explicitly logged degraded states, not a capability-consensus protocol
or a guarantee of identical depth after a broken peer installation. Native
continuation never waits for presentation repair.

Both intro movies ship separately beside the DLL in normal Debug/Release output,
the Windows installer and regular release ZIP. No video is embedded in the DLL;
only the small UI shader is embedded. Successful installation also retires the
exact old SBSv1 intro and two calibration MP4s. Native game data, foreign movies
and logs remain intact.

## Evidence and outstanding hardware review

The geometry harness tests actual production reprojection against independently
specified foreground/background/vertical image witnesses, including occlusion
and disocclusion. Duplicate-eye, reversed-depth, global-shift and vertical-flip
mutations fail. Shot gaps, lost ending and wrong source are rejected.
`verify_intro.py` checks all decoded frame addresses, dimensions, audio packets,
every consumed depth receipt/pin/hash, and compares every decoded left-eye frame
with its approved original using ffmpeg PSNR. It independently decodes the packed
output and compares right views at both sides of every segment and each midpoint
against their same-clock reprojection, including separation from duplicated left
pictures where the source provides a strong stereo witness. Those checks cannot prove correct
depth, comfortable fusion, motion quality, hand edges, decoder performance or
first-save continuation on a headset. Hardware evidence remains separate.

The current movie SHA256 is
`5514e50b99c5c70cb57c7887eb97ed333371f04a9eb6dbc7d731b40a3817fcca`
(93,361,756 bytes). Completed verification checks2931 original frame addresses,
2291 identical AAC packets,21 depth shots and78 encoded right witnesses. Minimum
native-YUV420 PSNR is35.32dB for all-frame left and36.06dB for sampled right.
The adjacent `GH_CP_Intro.sbs-v2.provenance.json` pins the actual movie, model,
executed generators and verification receipts. All21 scenic depth arrays and
geometry records are identical to the headset-approved687 revision. Only the
original wordmark planes change: a mask derived from the existing logo artwork
covers its printed edges with a constant field, feathered outside those edges.
The fire-title depth follows the original smooth reveal. The helper supplies
geometry only; final RGB always comes from the approved video. Thirteen focused
geometry checks include duplicated-eye, reversed-depth and excessive-depth
controls. Actual decoded opening/fire-title images were inspected separately.

Build687's preceding SBSv1 movie was93,329,919bytes/SHA256
`6929a4ad75df7ab7ea77cfed9630338110984e14d05b048b0fdf7c54709db93c`.
Its scenic appearance has the maintainer's explicit headset approval. The
stronger wordmark revision and additional native review/spawn paths still need
that hardware review; automated checks do not establish their headset picture.
The maintainer requires no additional archive delivery: the normal installation
script creates its regular ZIP from these separate Content files.

# Stereo video calibration (developer pilot)

This offline developer fixture proves a media contract before any 2D-to-3D
conversion is attempted. The picture is drawn from explicit geometry in Python;
there is no inferred depth, generative imagery, paid call, game asset or audio.
Automated measurements do not establish correct Unity eye routing or headset
comfort.

The fixed profile `ghvr-stereo-calibration-v1` contains 540 frames at exactly
60 fps (9 seconds), with cuts at frames 180 and 360. `calibration-sbs.mp4` is
full side-by-side H.264, 3840×1080, left eye first, with a complete 1920×1080
picture for each eye. `calibration-mono.mp4` is a 1920×1080 center view with
the same frame addresses. Both use square pixels, 8-bit YUV420, silent video,
BT.709 tags, and a keyframe every 60 frames. Display aspect must be 16:9 per
eye, not the packed file's 32:9. The files intentionally do not depend on
stereoscopic container metadata.

Every picture has bilingual English/German eye, orientation and frame labels.
The grid is identical in both eyes and lies on the screen plane. Three solid
markers move together on an integer triangle clock. Their disparities, defined
as `x_left - x_right`, are +12 pixels (near), zero (screen), and −12 pixels
(far). This is 0.625% of the 1920-pixel eye width. Pixels alone cannot establish
angular disparity or comfort: physical window width, viewing distance, IPD,
the projection and the observer still matter. Start with a moderate physical
window and stop the hardware test if fusion is uncomfortable. No universal
comfortable distance or physical depth is claimed.

The asymmetric top corner shapes and readable orientation labels reveal
flips or mirrors. Both eyes carry the same absolute frame counter and shot
counter, alongside a wide-cell machine-readable code with a distinct eye ID.
The last frame is 0539; restarting the clip deliberately cuts to frame 0000.
The fixture is not a seamless ambient loop.

Run from a checkout with Python 3.11+, Pillow 10.1+ (`load_default(size=...)`),
ffmpeg with libx264, and ffprobe. Use a new output directory each time:

```bash
python3 scripts/stereo-video/build.py \
  --out-dir "$PWD/.planning/debug/stereo-video/run-calib001"
python3 tests/stereo-video-tools/check.py \
  --media-dir "$PWD/.planning/debug/stereo-video/run-calib001" \
  --out-dir "$PWD/.planning/debug/stereo-video/run-check001"
```

`calibration-manifest.json` records file sizes/SHA256, source hashes, tool
versions, dimensions, frame count, duration, exact encode commands, cut frames,
pixel checks and seeks. Source PNG witnesses cover both sides of each cut and
the final frame. Deterministic source drawing uses Pillow's embedded default
font and integer geometry; byte-identical MP4 reproduction requires the same
recorded Pillow/ffmpeg/libx264 versions. There is no wall-clock timestamp in
the picture or encoder metadata.

The validator decodes both delivered files, checks every presentation timestamp
and all 1,620 eye pictures (1,080 stereo plus 540 mono), reads 35,640 code cells,
and measures 4,860 marker centers. It independently seeks to frames 0, 179,
180, 181, 359, 360, 361 and 539 in each file. The focused test includes twelve
causal controls: swapped eyes, either duplicated eye, an old right-eye frame at
a cut, bad rate, size, pixel aspect, cut keyframe, timestamp, decoded count,
wrong depth with intact labels, and a real MP4 remux with doubled timestamps.
Eleven controls mutate the measured decoded witnesses or probe observations;
the twelfth is decoded from an actually remuxed file. These controls certify
the offline validator, not the runtime compositor.

The generator and test reap or kill/reap their own subprocesses before calling
the repository's `cleanup_completed_run` API on success, failure or interruption.
No project build cache or extracted frame sequence is generated. Current movies,
source PNG witnesses, manifests, logs and results are deliverables/evidence and
are retained; the temporary clock-control MP4 is removed. To obtain a recognized
retention receipt, use a `run-` directory with eight lowercase letters/digits/
underscores below this checkout's `.planning/debug/`. When a worker writes to
the main checkout, pass `--retention-checkout /home/claw/gloomhaven_vr`.

The original generation receipts are in the main checkout's ignored
`.planning/debug/stereo-video/calibration-v1/`. The reviewed MP4s are now tracked
under `src/GloomhavenVR/Assets/Movies/Calibration/` and are normal content files:
Debug/Release output, the regular installer and release ZIP place both files at
`Movies/Calibration/calibration-{sbs,mono}.mp4` beside the plugin DLL. They are
**separate files, not embedded in the DLL**, following the maintainer's explicit
2026-10-17 clarification. The source-hashed provenance identifies the unchanged
reviewed media.

Start the runtime pilot with **VR Options > Advanced > Start stereo video test**
(German: **VR-Optionen > Erweitert > Stereo-Videotest starten**). The old
`--ghvr-stereo-video-calibration` argument no longer starts it and can be removed.
The action displays live localized status, including preparation, playback,
mono fallback, missing media and failure. After closing or a failure, press the
same button again to request a fresh attempt; no application restart is needed.
A request outside the offline VR main menu is refused visibly and is never
queued for a later scene. Repeated presses during preparation/playback retain
the same decoder and fixed picture.

Hardware review must still check the left eye sees LEFT, the right eye RIGHT,
text and the grid fuse at the screen plane, near/far order is correct, both
counters agree through cuts/seeks, the per-eye aspect is preserved, and closing
or skipping leaves native flow usable. Record build/version banners, XR mode,
window size/distance, and actual headset observations separately from these
offline receipts.

## Runtime pilot

Each deliberate menu-button press can start the pilot in the offline native
main menu when VR and world UI are active. It owns a separate silent decoder and a fixed picture,
1.2 real metres wide at1.6 metres distance. The picture opens and closes manually
with a short fade; a click on its actual image requests closure. Menu/session
exit, errors and shutdown release resources directly without holding native
continuation. Completion never automatically restarts; another button press can
start a fresh attempt. Closing the options menu itself does not close the picture.

The tiny UI shader is embedded in the plugin DLL. It requires the actual runtime
MultiPass mode and selects the half from each camera's active eye. Paired camera
callbacks restore an enclosing eye after nested camera rendering. Unsupported
shader/mode preflight selects the separate mono movie; absent or malformed
calibration media stops the attempt and reports its reason. The normal launch
does not inspect these files or load the shader.

The calibration decoder does not replace or publish a native movie. Ordinary
intro, character, menu and embedded films keep their existing presentation during
this milestone. The later film-library integration requires a versioned media
and peer-selection/fallback contract after the headset pilot passes. See the
current [build evidence](../.planning/STEREO-VIDEO-683.md) and the renderer's
[API and GPU-proof scope](../scripts/stereo-video-render/README.md).

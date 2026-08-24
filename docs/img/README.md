# README images

## `logo.png` — the wordmark on a dark plate, and the "white gaps" that were never a file defect

The user reported white gaps in the wordmark **on GitHub only**. Two rounds looked for a file or an
encoding fault and found none. It is neither:

- `logo-light.png` was byte-exact against the artist's artwork composited on white — **max
  difference 0 on every channel of every pixel**, and the same for `logo-dark.png` on `#0d1117`.
- On the fully-opaque pixels the two shipped copies were **identical to each other**, so nothing
  about compositing could be producing a difference between the two themes.
- The PNG itself is unremarkable: 1024×179, 8-bit, colour type 2, non-interlaced, no `gAMA`,
  `sRGB` or `iCCP` chunk to be mis-read.

**The pale patches are IN THE ARTWORK.** The letter interiors of GLOOMHAVEN are filled with a light
parchment tone; against black — the game's main menu, and any viewer with a dark canvas — that
reads as lit metal, and against white it reads as a hole. The report was accurate and the diagnosis
was contrast inversion, not corruption. The reason it appeared "only on GitHub" is that GitHub's
light theme was the only place the wordmark was ever put on a white page.

ModBuild 248 answered that with a dark plate behind the wordmark. **The user rejected the plate**
("ich will es transparent, das nur das logo ohne hintergrund sichtbar ist"), so what ships is the
artwork itself with an alpha channel and nothing behind it: `logo.png`, one file, both themes.
`logo-light.png` / `logo-dark.png` are gone.

**The consequence is accepted, not forgotten.** On GitHub's light theme the pale letter fill will
read as gaps again, because that is what this artwork does on white. If that ever has to be
solved, the answer is a BACKING on that theme only — **never a repaint of the letter fill**, which
would mean redrawing the artist's wordmark to survive a canvas it is barely shown on.

**Regenerating it:** `logo()` in `unity/asset-preview/build_readme_images.py` — the source is
`src/GloomhavenVR/Assets/GloomhavenVR_logo.png`, still the same file the mod puts in the main menu,
so there is one source of truth for the artwork.

## The demo clips are MP4, not GIF, and that was measured

The README is the project's pitch and these clips carry it, so they matter more than any paragraph
on the page. They were GIFs for exactly one day. The two that exist were captured as 15-second
1280x720 clips and encoded both ways:

| | card-fan | figure-grab | together |
|---|---:|---:|---:|
| GIF, as first produced | 33.4 MB | 38.2 MB | **71.6 MB** |
| GIF, re-encoded hard (15 fps, 720 px, 128 colours) | 14.7 MB | 17.4 MB | 32.1 MB |
| WebM VP9 | 1.17 MB | 1.22 MB | 2.4 MB |
| **H.264 MP4, 960 px, 30 fps, CRF 26** | **1.17 MB** | **1.33 MB** | **2.5 MB** |

Roughly **29x smaller than the GIFs at twice the frame rate and full colour**, because a GIF has
256 colours, no interframe prediction and no chroma subsampling, and a dark scene with fine
particles is the worst case for all three at once. The optimised GIF is still 32 MB *and* looks
worse. This is not a close call; do not re-add a GIF.

**Git LFS is not the answer either**, and the reason is worth writing down: a plain `git clone` of
an LFS repo still downloads the CURRENT version of every LFS file — the smudge filter is the
default and skipping it is the cloner's choice, not the repository's. LFS saves HISTORY, not the
working copy, so for a clip committed once it saves nothing, while spending the account's
1 GB/month LFS bandwidth on every clone.

## How they are embedded — a POSTER that links to the file, and why `<video>` lost

The first attempt used `<video src="https://github.com/OWNER/REPO/raw/main/…">`. The user saw
nothing, and there were TWO independent reasons, either of which alone is fatal:

1. **The URL named `main`.** The clips are committed on `dev`; `main` is the release branch and did
   not have them. The tag pointed at a 404.
2. **The repository is private**, so a `raw` URL needs authentication and a `<video>` element has no
   way to ask for it. It renders empty even when the path is right.

And a third that is not GitHub's fault: `<video>` is only conditionally allowed through GitHub's
HTML sanitiser, so a tag that works today is not a thing to build a pitch on.

What ships instead is a **poster image that links to the mp4**:

```html
<div align="center">
  <a href="docs/img/card-fan.mp4"><img src="docs/img/card-fan-poster.jpg" width="800"></a>
</div>
```

A relative `<img>` is rewritten by GitHub on **every** branch, public or private, and has never
needed a sanitiser exemption. Posters carry a play glyph drawn on top so the still reads as a video.

**But the link only reaches GitHub's blob page**, where the reader has to press *View raw* and the
file downloads — the user's report, and a fair complaint.

### The only way a video actually PLAYS in a GitHub README

It has to be served from GitHub's **attachment CDN**, not from the repository. A repo-relative path
and a `raw.githubusercontent.com` URL both refuse to play; nothing you can commit will play by
itself. The URL has to look like:

```
https://github.com/user-attachments/assets/<uuid>
```

and you only get one by **uploading the file through a comment box**: open a new issue, a PR, or a
discussion in this repository, drag `docs/img/card-fan.mp4` into the text area, wait for the upload
to finish, and copy the `https://github.com/user-attachments/assets/…` URL it writes into the box.
**Do not submit the issue** — the upload has already happened and the URL is permanent. Then either
put that URL bare on its own line, or wrap it:

```html
<video src="https://github.com/user-attachments/assets/xxxxxxxx" controls muted loop></video>
```

Notes that matter here:
- **Limits are fine for us**: 10 MB through the web editor, 25 MB through a comment box. Our clips
  are 1.2 MB and 1.3 MB.
- **MP4/H.264, MOV and WebM** are the accepted formats. Ours is already MP4/H.264.
- **While this repository is PRIVATE**, an attachment URL still needs the reader to be signed in
  and permitted — so it will play for the maintainer and be blank for a stranger, and it starts
  working for everyone the day the repository goes public.
- The file then lives OUTSIDE the repository. Keep the committed `docs/img/*.mp4` as the durable
  copy: it is the thing a clone carries, and the attachment can be regenerated from it.

**THIS IS NOW DONE.** The user uploaded both clips through a comment box and handed back the two
`user-attachments` URLs, which sit bare on their own lines in the README — that is the whole
embed, GitHub turns a bare attachment URL into a player. The poster JPGs and the
`docs/img/*.mp4` files stay committed: the mp4 is the durable copy a clone carries and the one
an attachment can be regenerated from, and the posters are the fallback if the attachment CDN
is ever not an option.

**Which URL is which was taken on the user's word**, in the order he sent them (card-fan first,
figure-grab second). It cannot be checked from here — an attachment URL on a private repo 404s
without a session — so if the two clips ever appear under the wrong headings, that is the
reason and swapping the two lines is the fix.

## Encoding a new clip

```
ffmpeg -ss <start> -to <end> -i <capture>.mp4 -an \
       -c:v libx264 -crf 26 -preset veryslow -pix_fmt yuv420p \
       -vf "scale=960:-2,fps=30" -movflags +faststart docs/img/<name>.mp4
```

- **`-an`** — the clips are silent on purpose. A README video is watched muted, and one that could
  suddenly play sound is worse than one that cannot.
- **`-pix_fmt yuv420p`** — without it Safari and several Android browsers will not decode it.
- **`-movflags +faststart`** — puts the index at the front so it starts on a partial load.
- **Trim the capture.** A Virtual Desktop recording opens and closes on the VD dashboard and shows
  the controller models before the hands take over; both ends have to go. Find the cut by eye:
  `ffmpeg -i <capture>.mp4 -vf "fps=4,scale=400:-1,tile=5x4" sheet.png` and read the sheet.
- Keep each clip **under ~3 MB** and around **10-15 seconds**, one idea per clip, cropped to the
  action. Capture ONE eye — a stereo capture is twice the pixels for no benefit on a flat page.

## The asset strips

`styles-hands.png`, `styles-masks.png`, `styles-boards.png` are rendered from the **shipped bundle
assets** by `unity/asset-preview/render_asset.py`, which reproduces `GloomhavenVR/BoardLit` node for
node — the same emission-of-albedo-times-shade the player sees, with no renderer lighting model
getting a say. A strip is not concept art; it is the asset.

**THE FIRST STRIPS WERE WRONG IN THREE WAYS AT ONCE** — the user's report was "die Renderbilder von
den Masken und Händen sehen kaputt aus, nicht so wie sie im Spiel zu sehen sind ... zB die Finger
bei den Händen". He was right about all of it, and the textures were the one thing that was not the
problem: the script has always bound the same loose PNGs the shipped `.mat` files bind, by GUID.
What was wrong was everything around them.

1. **`blend_method = 'BLEND'` broke the fingers.** Every hand and mask albedo in this bundle is
   FULLY OPAQUE — alpha is 255 at all 2048x2048 texels, measured, on all six — so the alpha-mix the
   script built could only ever pass 1.0. It did nothing except put the material in Eevee's BLEND
   path, where depth writes are off and geometry sorts per OBJECT instead of per pixel. Four fingers
   in front of a palm is exactly the case that breaks under, so they drew through each other. Now
   `OPAQUE`, with no alpha term at all.
2. **Backface culling did not match the material.** Every shipped hand material carries `_Cull: 2`
   (Back); the script rendered double-sided, so each hand drew its own inside surfaces through
   itself. This one also hid the third fault from me: the arcane glove's cyan runes are on its BACK,
   and in the double-sided renders I was admiring them THROUGH the palm — which is why the first
   corrected render came out plain black and looked like a regression when it was the first honest
   picture. The masks are `_Cull: 0` and stay double-sided, deliberately.
3. **The masks are not lit at all.** They do not run BoardLit; they run `GloomhavenVR/HeadUnlit`,
   whose entire fragment stage is `albedo * tint`. Its own header says why: the head floats in the
   light-less VR void and in the mirror, where "any scene-lit shader (Standard, BoardLit's baked rig
   included) would either render black or add shading the albedo doesn't expect", because that
   texture already carries its own baked light. Putting BoardLit's Lambert on a pre-lit texture is
   double-shading — which is exactly why the first mask strip was dark and muddy and the corrected
   one shows a gold visor slit, lit runes and teal eyes. Pass `--unlit` for anything on HeadUnlit.

**Rule that follows from all three: read the shipped `.mat`, do not assume.** `_Cull`, the shader
GUID and the `_BumpMap` slot are all in there — the plate gauntlet's `_BumpMap` is `fileID: 0`,
i.e. no normal map, and passing one would have been a fourth wrong answer.

Two further things in that script are load-bearing, and both were learned the hard way:

- **`view_layer.update()` before reading `cam.matrix_world`.** Without it the matrix is still
  identity, the "camera-space" fit silently becomes a world X/Y fit, and the framing is right only
  for assets whose widest world axis is the one the camera happens to look at. That is exactly how
  the first run framed all three boards correctly and cut the fingertips off all three hands.
- **`--scale` and `--focus-z`.** Auto-fit makes every asset fill its own frame, which normalises
  away real size differences and lets a strip claim something untrue. The hands are pinned to the
  sizes the player actually sees: the armoured hands are ~2.3x bulkier in mesh and are worn at
  `[Hands] PlateScale` 0.62, the number that matches them to the glove's real hand bulk.

**THE RECIPE IS NOW A SCRIPT, because it was once only in a shell history.** The strips were
rebuilt at ModBuild 247 and the exact per-asset flags lived nowhere, so the next asset delivery
could not reproduce the frame it was replacing. `unity/asset-preview/build_asset_strips.sh` holds
them: yaw 215 (the BACK of the hand, the side every set carries its decoration on), `--cull` to
match `_Cull: 2`, `--normal` for all three sets, and `--mrs` for the plate. Then
`ASSET_RENDER_DIR=<dir>/ python3 unity/asset-preview/build_readme_images.py` flattens onto
`#1a1613`.

**ModBuild 248 added `--mrs` to the renderer for one reason: A PREVIEW THAT OMITS A TERM AGREES
WITH EVERY BROKEN BUILD.** BoardLit gained an opt-in specular lobe so the plate gauntlet's
delivered metallic/roughness maps have a consumer; a strip rendered without it would show the flat
grey steel that motivated the shader change and would keep showing it however the shader was tuned.
The node graph is the shader's arithmetic verbatim. Note what a STILL cannot show: a highlight is
view-dependent, so on one frozen frame it is a broad sheen (measured: 27,647 pixels lifted, +40/255
at most, +7 on average) and only a hand that TURNS makes it read as metal. That judgement is the
hardware round's, not the strip's.

## The environment images

`env-cellar.jpg`, `env-forest.jpg` and `env-elements.jpg` are **preview renders of the shipped
environment prefabs**, not screenshots — the same `EnvironmentsPreview` harness every environment
lane is judged on, so a picture on the front page can be reproduced and re-checked instead of being
a photograph nobody can take again.

```
ENV_PREVIEW_OUT=<dir> ENV_PREVIEW_VIEWS=Readme ENV_PREVIEW_NOHAUNT=1 ENV_PREVIEW_NOFIRE=1 \
  xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode \
  -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
  -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-readme.log
ENV_RENDER_DIR=<dir>/ python3 unity/asset-preview/build_env_images.py
```

**Both stations are shot from the player's own head height** — `ReadmeC` at 2.02 m in the cellar and
`ReadmeS` at 2.80 m in the wood, the same `HeadCellar`/`HeadForest` constants THE HEAD SET uses. A
marketing shot taken from a camera the player never occupies is a lie that costs nothing to avoid,
and the only thing they change against `HeadC`/`HeadS` is the AIM: those two look at the darkest
half of their room on purpose, because that is what they were added to judge.

**Nothing is re-lit, re-graded or brightened in the compositor.** Both rooms are night scenes with a
handful of small sources and that IS the product; a curve pulled over them for the page would
advertise a picture the player never gets. `build_env_images.py` only crops, resizes and draws the
caption bar on the element grid.

### The element grid is a BEFORE/AFTER, and the first version of it was not

The first `env-elements.jpg` was four single frames of four different element states. The user's
verdict: *"nicht wirklich ersichtlich was da der unterschied ist"* — and he was right, because
there was nothing in the picture to compare each cell against. The hero images sat far above it at
a different aspect ratio, so every cell was just a dark room.

Every row now shows **the same camera with the element off and on**, side by side, and the four
rows are chosen for the SIZE of the difference rather than for covering the set:

| Row | What changes |
|---|---|
| Cellar · Fire | the whole room lifts to the ceiling — the biggest single change either room makes |
| Cellar · Ice | frost grows out across the flagstones, cold blue against the candle |
| Forest · Light | canopy, ferns and shafts all lift out of the dark |
| Forest · Dark | the moon goes into total eclipse and turns copper; the wood goes black |

**Air and Earth are deliberately not in it.** They are real and they are MOTION — leaves shaking,
growth coming up — and a still cannot show either honestly.

**The Dark row is a CROP** (360×202 of the 1280×720 `ReadmeMoon` frame) because the eclipse is the
most visible thing on either element board and at README width the disc would otherwise be a few
dozen pixels. Cropping a real frame is not staging it; re-lighting one would be.

**The moon is partly behind foliage and that stays.** A 2.2 m lateral offset was tried on the
parallax argument — the moon is at infinity, the crown in front of it is metres away — and it made
the occlusion *worse*, because every direction out of the centre walks under another tree. The
file's own note says the same thing ("from inside the clearing the crowns cover most of the moon").
A partly-veiled moon is what the player gets.

All four use the fixed non-zero clock (3.7 s) the element review set uses, and the `ReadmeMoon`
station was ADDED to the Views table — nothing above it moved. Do not move an existing station to
get a nicer frame; fifty review frames hang off each one.

## Still missing

Each has a visible placeholder in the README at the spot it belongs; dropping the file in with the
exact name below makes its `<!-- VIDEO: … -->` comment the line to replace.

| File | What it should show |
|---|---|
| `overview.mp4` | The hero shot: standing at the table in a lit scenario, then dragging, rotating and zooming the board with the two-handed world grab. This is the one that has to sell it in three seconds. |
| `multiplayer.mp4` | **The one that matters most now.** Two players at the same table: masks and hands, a miniature lifted and seen by both, a shared window dragged to a new place in the room. It needs two headsets, which is why it is not here yet. |
| `windows.mp4` | A window opens in front of the player, is grabbed by its bar, moved and resized, then reeled closer with the thumbstick. |
| `map-room.mp4` | The 3D campaign map room: pressing a table-rim cap, pointing at a location, the party token walking its route. |
| `environments.mp4` | The cellar and the night forest — firelight, the night sky, foliage moving, an element infusion changing the room, switching environments in the settings. The three stills already ship (see above); what a clip adds that they cannot is the MOTION: the drip, the rat, the shafts, and an element fading in over its second. |

Each new clip needs a poster beside it, same name plus `-poster.jpg`.

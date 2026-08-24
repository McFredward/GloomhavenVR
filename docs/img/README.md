# README images

## The logo — MEASURED, three rounds in: the file is fine, the artwork is drawn for black

The user reported white gaps in the wordmark **on GitHub only**, and asked a third time why he sees
it nowhere else. So it was finally measured properly instead of argued about:

**His GitHub screenshot was cropped to its ink box, scaled to ours, and differenced against the
artist's original composited on white.** Median difference **2/255**, mean 6.8, and only 2.9 % of
pixels off by more than 40 — screenshot noise, nothing else. GitHub renders our file correctly, and
it always did. (Earlier rounds had already shown the shipped copies were byte-exact against the
artwork on both canvases, max difference 0; what was missing was the comparison against *his* pixels.)

**The wordmark is drawn for a dark background.** Its letter fill and its outer bevel are a light
parchment tone. Against black they read as lit metal; against white they lose nearly all their
contrast and the letters look hollow. GitHub's **light theme** is simply the only place this
wordmark is ever shown on white — the game's main menu and every local image viewer put it on dark.
That is the whole of "warum sehe ich das nur in GitHub".

### What ships

Two copies, picked by `<picture>` + `prefers-color-scheme`:

| File | Served to | Why |
|---|---|---|
| `logo.png` | dark theme (the default, and what the user reads) | **transparent**, no backing — what he asked for, and correct there |
| `logo-onlight.png` | light theme only | the same artwork on a dark plate, because the transparent one breaks on white |

**Neither repaints a pixel of the artist's wordmark**, and nothing may. Brightening the letter fill
so it survives a white canvas would mean redrawing his artwork to fit one theme of one website.

**Do not "simplify" this back to one file.** One transparent file breaks on light; one plated file
was rejected by the user ("ich will es transparent"). The two-file split is the only arrangement
that satisfies both, and it costs one `<picture>` element.

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
`user-attachments` URLs.

**They are now `<video>` tags in a two-column table**, not bare URLs on their own lines, because
the page was too long and the user asked for them side by side ("skalier die Videos dass sie etwas
kleiner sind oder nebeneinander"). THIS IS THE ONE THING ON THE PAGE THAT IS NOT GUARANTEED: a bare
attachment URL on its own line is turned into a player by GitHub's own Markdown pipeline and always
works, whereas `<video>` goes through the HTML sanitiser, which allows it but has never promised
to. If the two clips ever render as nothing, that is the cause and the fix is to put the two URLs
back on their own lines and accept the height. The poster JPGs and the
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

## `styles.png` — the nine assets as a 3x3 matrix

One image: three rows (hands, masks, boards), three columns, each cell one asset with its name
underneath, on **white**. All three properties are user rulings:

- *"Fass dich noch kürzer in der README"* — one image instead of three images plus three caption
  lines.
- *"mach die Assets so dass sie untereinander stehen, wie eine Matrix"* — the cells line up in
  COLUMNS as well as rows. That is why `styles_matrix()` builds from the **nine individual
  renders**, not from the three `styles-*.png` strips: a strip centres its three items inside its
  own width, so three strips stacked can never line up with each other.
- *"mach den Hintergrund weiss statt schwarz (es soll trotzdem lesbar bleiben)"* — white, with
  DARK labels. Nothing about the renders changed: the arcane glove and Grimhorn are nearly black
  and read perfectly well on white, which is exactly what a transparent render is for.

**ONE SCALE PER ROW, never per cell.** Auto-fitting each asset to its own cell would normalise away
the real size differences inside a family and let the picture claim something untrue.

**The order inside each family is the user's** and is not alphabetical, not file order and not
obvious: hands as delivered, masks **Grimhorn · Ironwatch · Runeveil**, boards **Steel · Bronze ·
Oak**. The file names do not help — `Mask_0` is Ironwatch, `Mask_1` Runeveil, `Mask_2` Grimhorn
(`Loc.cs:1018-1020`), and `PlayTray_9capjqp6` is STEEL while `PlayTray_16vm268h` is BRONZE
(`BoardFrame.cs:68-69`). Those two have been swapped once already; check the source, never the
look of the render.

The three `styles-*.png` strips are still built and still committed — they are the per-family
image and remain useful — but the README shows only the matrix.

## The asset strips## The asset strips

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

**THE CONTROL BOARDS WERE WRONG IN THREE WAYS AT ONCE** (user: "die Controlboards werden nicht
richtig gerendert in der README, sie sehen dort kaputt aus"), and all three were in the flags, not
in the assets:

1. **No normal map.** All three tray materials bind one and always did. Without it the carved
   recesses, the wood grain and the brass corner straps have no relief and a tray reads as a
   painted plank.
2. **Pitch 18 is edge-on.** A tray lies FLAT. Shot from 18 degrees it is a sliver; the player looks
   down at it, so 50 does what his eye does.
3. **No shared scale.** All three are 639-640 mm wide in the room but their depth differs
   (318 / 369 / 218 mm), so auto-fit sized each to its own bounding box and the strip claimed the
   brass one was half again as big as the others. `--scale 0.78` pins all three to one ortho width.

Note the boards are **`_Cull: 0`** (double-sided), unlike the hands' `_Cull: 2` — so they are
rendered WITHOUT `--cull`, and that is correct rather than an oversight to be "fixed" by copying
the hand line. Read the `.mat`. And the names swap easily and have been swapped once:
`PlayTray_16vm268h` is **bronze**, `PlayTray_9capjqp6` is **steel** (`BoardFrame.cs:68-69`,
`VRCardFactory.cs:29-30`) — check the source, not the look of the render.

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
| Cellar · Fire | the same corner goes from two quiet barrels to a burning spill, flames on the floor and stone glowing red |
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

**The Fire row is shot at a fire, not from the README station.** The user's verdict was "das Feuer
sieht man nicht auf dem Bild", and he was right: `ReadmeC` looks at the candle table, so under Fire
it showed the room BRIGHTENING with no fire anywhere in frame, which reads as a light switch. The
gated fires are props in fixed places and a frame has to point AT them. **A row does not have to
share a camera with the other rows** — it has to share one with its own other half, which is the
only comparison being made.

**It is `FireSpill` and not `FireRoom`, on his next report:** *"beim Feuer-Render sieht man solche
Kreise um das Feuer herum — das sehe ich ingame nicht."* Those circles are the `Env_GlowSphere`
shells (`EnvGlow.shader`): real scene geometry, an additive sphere whose brightness falls off
toward its own silhouette. In the headset they read as volumetric haze around a flame; on a flat
still shot from across the room they read as two hard-edged discs on the masonry. **No cause is
asserted beyond that** — it was not investigated as a defect, because the user reports the headset
looks right and this file's own note already says previews come out brighter than the headset. What
changed is the FRAME, not the room: `FireSpill` stands close to a burning spill where the flames and
the glowing floor fill the picture and no glow shell is seen edge-on against a wall. **Never re-light
or re-grade a room to make a screenshot behave.**

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

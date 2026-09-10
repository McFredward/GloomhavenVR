# README images

> Not everything here is a README image any more. `controls-*.png` and `install-tree-*.png` are
> **diagrams built by a script in this directory**, and they exist because the user-facing docs were
> reworked to carry their information in pictures rather than prose: *"halte es kurz und knapp ...
> Arbeite auch mehr mit Bildern! ... Die Zielgruppe sind User mit einer kurzen
> Aufmerksamkeitsspanne."* They are documented at the bottom of this file.

## `promo.gif` — the page header

Both READMEs open with `docs/img/promo.gif` (7.6 MB, 800x571, 154 frames) — the artist's promo
animation, which replaced the wordmark as the header on 2026-08-24. It is the largest file in this
directory by a wide margin: more than everything else here put together.

**Rebuild it with `unity/asset-preview/round_promo_gif.sh`, never by hand.** The script's whole
job is rounding the corners *without paying for them in bytes*, and its header records the six
routes that were measured to get there — the naive ones cost between 12 MB and 65 MB for the same
picture, because a GIF's transparency index is also the encoder's only inter-frame lever, and PIL
re-encoding the source **without changing a pixel** already costs 32.8 MB. Read that comment before
reaching for ffmpeg or PIL. Source: `.planning/debug/ressources/gloomhavenvr_promo.gif` (gitignored;
the artist's delivery). Output: `docs/img/promo.gif`.

## The logo — RETIRED as the page header, kept as the measurement record

> **`logo.png` / `logo-onlight.png` are no longer in any README.** `promo.gif` took the header on
> 2026-08-24 and the `<picture>` element went with it — `grep -n "<picture" README.md README.de.md`
> returns nothing. Both files are still built and still committed on purpose: the measurement below
> is the answer to a question that was asked three times, and it will be asked again the next time
> anybody looks at that wordmark on a white page.

### The measurement: the file is fine, the artwork is drawn for black

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

### What was served, while it was served

Two copies, picked by `<picture>` + `prefers-color-scheme`:

| File | Served to | Why |
|---|---|---|
| `logo.png` | dark theme (the default, and what the user reads) | **transparent**, no backing — what he asked for, and correct there |
| `logo-onlight.png` | light theme only | the same artwork on a dark plate, because the transparent one breaks on white |

**Neither repaints a pixel of the artist's wordmark**, and nothing may. Brightening the letter fill
so it survives a white canvas would mean redrawing his artwork to fit one theme of one website.

**If the wordmark ever returns to a page, do not "simplify" this back to one file.** One
transparent file breaks on light; one plated file was rejected by the user ("ich will es
transparent"). The two-file split is the only arrangement that satisfies both, and it costs one
`<picture>` element.

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

## Historical embedding attempts — repository video and poster links

The first attempt used `<video src="https://github.com/OWNER/REPO/raw/main/…">`. The user saw
nothing, and there were TWO independent reasons, either of which alone is fatal:

1. **The URL named `main`.** The clips are committed on `dev`; `main` is the release branch and did
   not have them. The tag pointed at a 404.
2. **The repository was private at the time**, so a `raw` URL needed authentication and a
   `<video>` element has no way to ask for it: it rendered empty even when the path was right.
   Public availability must be checked independently of branch and file existence. Changing
   visibility does not repair a wrong path; the current guides use attachment URLs.

And a third that is not GitHub's fault: `<video>` is only conditionally allowed through GitHub's
HTML sanitiser, so a tag that works today is not a thing to build a pitch on.

What used to ship instead was a **poster image linking to the committed mp4** — a relative `<img>`,
which GitHub rewrites on every branch and which never needed a sanitiser exemption, with a play glyph
drawn on top so the still read as a video:

```html
<div align="center">
  <a href="docs/img/some-clip.mp4"><img src="docs/img/some-clip-poster.jpg" width="800"></a>
</div>
```

**THAT ROUTE IS GONE AS OF 2026-09-07 AND MUST NOT COME BACK.** The link only ever reached GitHub's
blob page, where the reader has to press *View raw* and the file downloads. The maintainer reported
it twice; the second time he ruled it out entirely, and every committed mp4 and poster was deleted
with it. The only markup in this repository's docs now is the attachment `<video>` below.

### The only way a video actually PLAYS in a GitHub README

It has to be served from GitHub's **attachment CDN**, not from the repository. A repo-relative path
and a `raw.githubusercontent.com` URL both refuse to play; nothing you can commit will play by
itself. The URL has to look like:

```
https://github.com/user-attachments/assets/<uuid>
```

and you only get one by **uploading the file through a comment box**: open a new issue, a PR, or a
discussion in this repository, drag the mp4 into the text area, wait for the upload
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
- **The repository is PUBLIC now**, so attachment URLs play for everyone. This bullet used to warn
  that a stranger would see a blank player until the repository went public; that day has come and
  the warning is spent.
- The file then lives OUTSIDE the repository, and **that is now the only copy** — see *The clips
  that exist — NONE* below. This bullet used to say "keep the committed `docs/img/*.mp4` as the
  durable copy"; there are no committed mp4s any more and adding one back is the thing the
  2026-09-07 ruling forbids.

**THIS IS NOW DONE.** The user uploaded both clips through a comment box and handed back the two
`user-attachments` URLs.

**Clips are `<video>` tags, not bare URLs on their own lines**, because the page was too long and
the user asked for them side by side ("skalier die Videos dass sie etwas kleiner sind oder
nebeneinander"). THIS IS THE ONE THING ON THE PAGE THAT IS NOT GUARANTEED: a bare attachment URL on
its own line is turned into a player by GitHub's own Markdown pipeline and always works, whereas
`<video>` goes through the HTML sanitiser, which allows it but has never promised to. If a clip
ever renders as nothing, that is the cause and the fix is to put the URL back on its own line and
accept the height.

(The two sentences that stood here about `card-fan` and `figure-grab` being served from committed
mp4s and posters were retired with the files: neither clip, neither poster and no `docs/img/*.mp4`
exists. The README carries six attachment clips today.)

**Verify each attachment before changing its placement.** Open the supplied URL and inspect
the footage; a filename or the order of a message is not proof of its content. Before public
release, also verify playback without a signed-in repository session.

## Committed gameplay clips — none

**Gameplay MP4s are hosted as attachments.** The animated `promo.gif` header remains committed.
Every former gameplay MP4 and its poster went on
2026-09-07 — `control-board` first, then `card-fan`, `figure-grab`, `map-interaction` and
`physical-interaction` — and `figure-lift.gif`, the last survivor, followed the same evening.

The rule this directory runs on: **a video served from a repository path does not play on
github.com.** Not in a README, not in a doc, not as a `<video src>`, and a poster thumbnail linking
to one only gets the reader a download. The ONLY thing that plays is a file uploaded through a
GitHub issue or comment box and referenced by its `user-attachments` URL. Clips are never committed:
they are uploaded, and the docs carry the URL.

**`figure-lift.gif` was the one exception and the maintainer retired it himself**, verbatim: *"Das
Aufheben einer Figur soll auch als Video und nicht als gif existieren. Entferne das gif wieder."* It
had existed for one reason — a GIF at a repo path DOES render inline, so it was the only way to show
the figure lift without an upload. He uploaded the footage instead, so the reason is gone and 4.7 MB
went with it. `promo.gif` at the top of the README is now the last moving image in the repository,
and it is a title card rather than a clip.

WHAT IS LEFT IN THIS DIRECTORY is stills — the controls picture, the board diagram, the environment
and style shots — plus `env-default-surround.png`, which is not documentation at all but a build
input for the environment picker's Default tile.

`physical-interaction` WAS trimmed at **0.55–13.10 s** of its capture, and the lesson outlives the
file: both ends had to go, the cut points were read off a contact sheet and then checked frame by
frame — the controller model reappears at 11.60 s, which a 2 fps sheet alone does not show. Every clip cut since has had the same
defect at one end or both: the Virtual Desktop overlay, the Quest status bar and its app tiles.
Cut at the frames where it clears, and check the first and last frame of the RESULT, not of the
source.

`figure-lift.gif` WAS a derivative rather than a capture — 4.4–10.9 s of `figure-grab.mp4` at 10 fps,
560 px, 96 colours with a diff-mode palette — and the encoding is recorded because the technique is
the fallback whenever a moving image is needed and no upload has happened. It is not needed now: the
figure lift is an attachment `<video>` at the top of the README, next to the overview.

**THE CLUSTERS ARE HISTORY — the README no longer draws any of these four.** They were arranged in
two pairs on the user's instruction ("Ordne die Videos logisch in zusammenhängenden Clustern"), and
between 2026-08-24 and 2026-09-07 every pair was replaced by a single fresher clip served from the
attachment CDN. The pairing reasoning is kept because it still governs anything that goes back:
each cluster was one sentence of prose plus two clips showing the same idea from two angles, so a
reader who watched one already knew what the other was about; four clips under one heading would
have read as four unrelated demos.

Where these files stand now: **all five are deleted**, and so is the GIF derived from one of them.
`control-board.mp4` went first that morning, then `card-fan`, `figure-grab`, `map-interaction` and
`physical-interaction` the same evening, each with its poster; `figure-lift.gif` followed once its
footage was uploaded.

The initial URL mapping came from the maintainer's upload order. Inspect the footage before
swapping a clip suspected of sitting under the wrong heading; public access is a separate check.

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
(`Loc.cs`, the `mask_name_*` entries — grep the key, the line numbers drift), and
`PlayTray_9capjqp6` is STEEL while `PlayTray_16vm268h` is BRONZE (`BoardFrame.cs:68-69`). Those two
have been swapped once already; check the source, never the look of the render.

**The three board cells do not come from `render_asset.py`.** Each board is shown *with its grab
rod*, and the rod is a procedural C# mesh — there is no FBX to hand Blender. So the board tiles are
rendered by `unity/GloomhavenVR.Assets/Assets/Editor/PreviewBoardAsset.cs` (see also
`PreviewGrabBar.cs`) and matched to the Blender renders by hand: 900 px, ortho 0.78, yaw 35,
pitch 50, transparent clear (`build_readme_images.py:181-197`). A `grabbars.png` sheet of the four
rods on their own was built first and then folded into this matrix; it is gone from the tree and
nothing references it.

The three `styles-*.png` strips are still built and still committed — they are the per-family
image and remain useful — but the README shows only the matrix.

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

## Additional recordings

The optional recordings and intended destinations live in
[VIDEO-SHOTLIST.md](../VIDEO-SHOTLIST.md). The READMEs contain six completed attachment
clips each; the playing guides link to those clips and have no video placeholders.
Update both languages when adding a recording. Keep MP4 uploads outside Git and follow
the encoding procedure above.

## The three built diagrams -- `controls-*.png`, `board-*.png` and `install-tree-*.png`

These diagrams ship in English and German, as do `env-styles-{en,de}.png`. Each diagram
generator reuses one artwork source and draws the localized words at build time; commit
the generated images alongside changes to the script.

```
python3 docs/img/build-controls-diagram.py     -> controls-{en,de}.png, controls-left-{en,de}.png
python3 docs/img/build-board-diagram.py        -> board-en.png,    board-de.png
python3 docs/img/build-install-tree.py         -> install-tree-en.png, install-tree-de.png
```

All three need Pillow and the Inter fonts at `/usr/share/fonts/opentype/inter`.

### `controls-{en,de}.png` and `controls-left-{en,de}.png` -- the controller maps

The default map explicitly names the right main controller; the `controls-left-*` map shows
left-main-controller bindings. Both use the same untouched source artwork and physical anchors.
`BoardPing.Tick` reads the main hand's lower face button (A on right, X on left), while
`NonDominantHold.Tick` supplies the other hand's lower button to `OptionsToggle` for pause.
The default movement/turn sticks stay physically left/right (`Defaults.Rig.cs`); handedness
alone does not swap them. Window reeling uses the carrying hand's stick (`PanelGrabHandle.ReelHand`
and `LaserCarryReel.OwnsStick`), correcting the previous diagram's right-stick-only claim.
Main-controller switching is documented in the picture: press that controller's trigger in the
main menu (`FlatScreen.TickHandednessSwitch`) or use VR Options → Dominant hand.
The generator checks callout geometry in all four variants before writing any output. Inspect all
four output images after generation; left-main mode swaps action colours with the X/A labels.

The single highest-value picture in the documentation set: the controls are what a new player needs
first, and prose is the worst possible carrier for a button map. It replaces two markdown tables and
about 250 words of the old `PLAYING.md`.

| Layer | Where it comes from |
|---|---|
| `controllers-artwork.png` | **generated** -- a transparent render of a Meta Quest Touch Plus pair with completely BLANK buttons, made with OpenAI `gpt-image-2`. Redrawn on 2026-09-06 (see below). 980x729, quantised to 200 colours: 427 kB to 52 kB, no visible loss at diagram size. |
| every ring, every word | **drawn by the script**, as real text in Inter |

**Never ask the generator for the labels.** A model cannot spell reliably, a labelled bitmap would
have to be regenerated per language by that same model, and a binding that moves could not be
corrected without redrawing the controllers. With the split, German is one more pass over the same
artwork and a moved binding is a one-line edit in `LABELS`.

**The callouts on the picture do NOT break that rule** -- the same reading `board-*.png` sets out.
What is forbidden is a word BAKED INTO THE GENERATED BITMAP; `CALLOUT_GEOM` is real Inter text drawn
by the script, per language, anchored to the same coordinates the rings are, each one a one-line
edit. They exist because the user asked for them in the same words he used for the board: *"Ich will
es auch so gestalten das eine Beschriftung im Bild schon vorhanden ist und man auf einem Blick schon
das meiste sieht so wie du es beim board auch gemacht hast."* The colour was the only bridge from a
ring to a legend cell, and a colour lookup is not a glance. The colour coding stays and each callout
wears its ring's colour, so the picture and the legend reinforce each other instead of being two
halves of a lookup. **Do not "fix" them back out.**

**The bindings in that script came from the SOURCE, not from the docs** -- `Rig/Comfort.cs`,
`Rig/WorldGrab.cs`, `Rig/SnapTurn.cs`, `Rig/Flight.cs`, `Board/BoardPing.cs`,
`WorldUI/Options/OptionsToggle.cs`, `WorldUI/Grab/NonDominantHold.cs`, `Defaults/Defaults.Rig.cs` --
because the docs were stale in three places when this was drawn, and a fourth was found when the
callouts went on: **The off-hand lower button opens the game's PAUSE screen, not an "options menu"**
(`OptionsToggle.cs:11-15`; the mod's options are one button inside it). If a binding moves, re-read
the source.

Five things in the script are load-bearing:

- **The feature coordinates** (`L` and `R`) are pixel positions in `controllers-artwork.png`, read
  off the render and verified by overlaying probe dots. Regenerating the artwork moves all ten --
  re-probe, never guess. Callouts and leaders are anchored in the **same** artwork pixel space
  (`AX` / `AY`), so they move with the rings when the page geometry changes and only a NEW ARTWORK
  moves them apart.
- **Both thumbstick entries share ONE colour.** A second colour on one physical stick read as a
  second button rather than as a second gesture; that was tried and it was worse. The picture
  instead gives that one ring **two callouts** in the one colour -- the push, named per hand outside,
  and the click, named once above -- so it can never imply the stick has a single action.
- **Every binding is named exactly ONCE, and the PLACE of the name is information.** Every part of a
  controller pair exists twice, and writing "Trigger" and "Grip" once per hand says nothing except
  that a pair is a pair. So: a binding that is that hand's alone (either stick's push, X, A) is named
  **outside**, beside its own controller; a binding that is identical on both (the stick click, the
  Y+B chord, the grip) is named **once in the lane between them, with a leader running to each
  hand** -- two lines out of one name is the picture saying "same control, both hands". The trigger
  is the single exception and the reason is geometric, not editorial: **both lane placements were
  built and both were rejected by the guard.** Level with the trigger the lane is at its narrowest,
  because the plates bulge inward above the necks, and the words land ON a controller; lower down,
  where they fit, the leader reaching back up to the trigger passes through the **grip** ring. So it
  is named beside the left hand and its WORDS carry the "either hand" its lines cannot.
- **`GAP` is 180 px because the lane carries words.** It was 28 px while every word lived in the
  legend, which is all the room a leader needs and nowhere near enough for a name.
- **`check_geometry()` is a guard, not documentation**, and it refuses to write either file if a
  claim fails -- both languages are measured before the first `.save()`, so a German failure can
  never leave a fresh English picture beside a stale German one. It asserts, in four families:
  the artwork is still 980x729 and **every ring still lands on the part it names** (probed out of the
  bitmap: the plate features must be dark under the ring, the trigger and grip light); the pair is
  laid out the way the callouts assume (mirrored halves, stick outboard of the lettered buttons, grip
  inboard of the trigger); every callout fits its gap in width **and in rows**, and the block it
  occupies stays on the page, clear of the legend rule, of the LEFT / RIGHT captions, of every ring,
  of every other callout and **off the controller silhouette itself** (a leader may cross the plate,
  a name may not); and **no leader passes through a ring it does not name**, which together with the
  silhouette check is what settled where the trigger's name goes.

#### The artwork must be a REAL Quest 3 controller -- 2026-09-06

The first artwork was generic, and the caption under the diagram said *"A Quest 3 is shown" /
"Abgebildet ist eine Quest 3"*. The picture contradicted the sentence, so one of the two was a lie;
the user asked for the picture to be fixed, because the Quest 3 is the most widespread headset.

**Prompt from photographs, not from memory.** The decisive feature is one nobody recalls correctly:
the top of a Touch Plus is a big matte **charcoal-black oval plate** overhanging a **white** body.
The old artwork, and the first four candidates generated from a written description, all made the
whole controller light grey -- which is the single most visible way to get a Touch Plus wrong. The
references used were the Wikimedia Commons photographs `Controller of Meta Quest 3 (left).jpg` and
`(right).jpg`; they also settle the face layout, which the diagram's rings are anchored to:

> thumbstick at the **outer** top of the plate; the two lettered buttons (X/Y left, A/B right) down
> its **inner** side on a diagonal; the unbound button -- menu on the left, Meta on the right --
> **below the thumbstick**; the index trigger on the front of the neck; the squeeze button a small
> protruding white nub on the **inner** side of the handle. No tracking ring: Touch Plus dropped it.

Seven candidates were generated and the winner picked by measurement, not by taste -- black plate
width as a fraction of the controller's own width (the plate is the widest part of the real thing),
face-button diameter as a fraction of plate width (~0.165 on the reference), and whether the trigger
and the grip nub are distinct enough to carry a ring. The chosen render is an exact mirror pair:
both halves came out with identical bounding boxes.

**Three preparation steps stand between the raw render and the committed artwork**, and a
regenerated artwork needs all three again:

1. **Clean the alpha.** `gpt-image-2` wraps the subject in a broad low-alpha white glow and reaches
   alpha 254 at most, never 255. Remap alpha linearly so everything under 60 becomes 0 and
   everything over 230 becomes 255; the silhouette's antialias ramp survives.
2. **Re-centre the halves.** Cut the two controllers apart and re-paste each one *centred in its own
   half* of the output. That is why `SPLIT_X` is now exactly half the artwork width, and why the
   `LEFT` / `RIGHT` captions no longer need the fudge offset they used to carry.
3. **Roll the highlights off.** The render's body tops out near luminance 245 on a 255 paper, and at
   diagram size the lower half of the handle dissolves into the page. Compressing 190..255 into
   190..230 keeps the controller off-white -- which is truthful, the real one is white -- while
   giving the silhouette an edge that survives the downscale to 860 px.

What is still not exact: the plate is rendered as a near-circle where the real one is a slightly
teardrop-shaped oval, and the handle is a little longer and glossier than the real part. The caption
stays because the claim it makes is one the picture keeps -- a reader holding a Quest 3 can match
every marked control on it, one for one.

### `board-{en,de}.png` -- the control board, explained the way the controllers are

The user asked for it in those words: *"Weiterhin will ich das du das Controllboard auch als Bild
zeigst mit entsprechenden Erklärungen ähnlich wie bei den Controllern."* So it is the same
language as `controls-*.png` -- artwork on top, a legend of colour-coded cells under it, and the
colour of each swatch matching a marker drawn on the artwork.

**It ships in the two playing guides only.** It started in the READMEs as well and the user moved
it: *"das board passt mehr in playing guide rein"*. The READMEs keep the paragraph about the board
and now link onward to the picture, so the front page stays a pitch and the map lives with the rest
of the instructions.

| Layer | Where it comes from |
|---|---|
| `board-artwork.png` | **rendered** -- the shipped Oak `PlayTray.prefab` with its grab rod, dead straight-on, through the real BoardLit material. Quantised to 200 colours: 3.2 MB to 240 kB. |
| every marker, every word | **drawn by the script**, as real text in Inter |

**The artwork is a render, not a screenshot, and not a Blender render either.** An in-game capture of
the board is the obvious candidate and it is unusable as a diagram base -- dark, with the player's
forearm across the board and a play glyph painted over the middle. (The specific file this used to
name, `control-board-poster.jpg`, was deleted on 2026-09-07 along with the clip it fronted; the
objection was never about that one frame.) And
`unity/asset-preview/render_asset.py`, which shot every other asset tile here, cannot draw the
**grab rod**: that rod is a procedural mesh built in C# at runtime, so there is no FBX to hand
Blender. So the artwork comes from a second entry point on the station that already solved exactly
that problem for the styles matrix:

```
BOARD_ASSET_OUT=<dir> xvfb-run -a Unity -batchmode \
    -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
    -executeMethod GloomhavenVR.BoardAssetShot.RenderDiagramArtwork -logFile x.log -quit
```

It writes `board_artwork_zneg.png` (2000x1150) plus a `_zpos` twin; **the `zneg` one is the
decorated face**, and both are written because which face carries the decoration is an FBX-import
question rather than something to assert from the authoring convention. `RenderDiagramArtwork` must
never be folded back into `RenderAll` -- that one's 900 px / ortho 0.78 / yaw 35 / pitch 50 are
matched to `build_asset_strips.sh` so the styles matrix reads as one picture, and all four are
wrong for a labelled diagram.

Three things in the script are load-bearing:

- **The marker coordinates are BOARD-LOCAL METRES, not pixels read off the render.** The controls
  script has to re-probe ten pixel positions whenever its artwork is regenerated; here the
  artwork's own frame is published by the renderer (`ortho 0.64960 x 0.37343 m`, centre printed at
  F5), so a re-render at another resolution needs no re-probe at all.
- **Read the anchors from the FBX, not from the Unity prefab.** The prefab's anchor transforms come
  out of `AssetDatabase` carrying a ~1.30x scale the board **mesh** does not have, which puts the
  key column at board-local 0.296 -- a centimetre outboard of three recesses that are plainly
  visible in the picture. The first cut drew its markers there and they missed everything. The FBX
  empties agree with `PlayTray.6.Build.cs`'s own fallback constants to within a centimetre, and
  that agreement is the cross-check.
- **The callouts on the picture do NOT break "every word is drawn at build time".** Read that rule
  precisely: what it forbids is a word BAKED INTO THE ARTWORK, because a model cannot spell, because
  a labelled bitmap would need regenerating per language, and because a label that moved could not
  be corrected without redrawing the picture. `CALLOUTS` is none of those -- real Inter text drawn
  by the script, per language, anchored in board-local metres, each one a one-line edit. They exist
  because the user asked for them: *"Ich will das ein kurzer Text direkt auch schon auf dem Bild zu
  sehen ist, damit man in einem Blick versteht welches Element was ist statt in der Legende erst ein
  mapping machen zu müssen."* The colour coding stays and each callout wears its marker's colour, so
  the picture and the legend reinforce each other instead of being two halves of a lookup. **Do not
  "fix" them back out.**
- **`ORIENTATION` is a guard, not documentation.** Seven claims the source makes about the board --
  slot 0 is left, the rest pads are left, the keys are right, short rest is the upper pad, Confirm
  is the top seat, the rod is below the bottom edge -- are asserted against the coordinates the
  markers are drawn from, and the script refuses to build if one fails. It exists because **the
  first render of this board was mirrored** and looked completely plausible until you noticed
  CONFIRM on the left.
- **`DOCK_CLAIMS` is the same idea for the zones that have no anchor empty.** `ORIENTATION` can
  check the seven recesses because the FBX carries an empty for each one. The active-card matrix
  carries none, so its nine claims are checked against the constants they were retyped from
  instead: the mount is `BoardW/2 + 0.012 + ActiveMountOffsetX`, it is outboard of the pile mount,
  it is three cards per row, the grid is symmetric about the mount in both axes, the rows overlap
  (`0.70` of a card height) while the columns do not (`1.06` of a card width), the whole zone is
  off the plate, it clears the pile plate by at least 8 mm, and it is inside the window. Two of
  those are worth the trouble on their own: **the column count was retyped as `2` in the peer
  mirror and stood for months** (`RemoteActiveCards`, fixed 2026-09-05), and a matrix drawn one
  size too large silently straddles the pile plate.

**The explanations came from the SOURCE, not from the docs** -- `Cards/Tray/PlayTray.*.cs`,
`Cards/BoardAnchors.cs`, `Cards/Caps/RestControls.cs`, `Cards/Piles/PileViewer.cs`,
`Cards/CardFan.cs`, `Cards/Driver/CardsDriver.*.cs`, `Net/Remote/RemoteElementStrip.cs` -- and two
labels would have been stale on arrival if they had not been:

- **there is no settings gear.** `PlayTray.1.Core.cs`'s own class doc still lists one in the right
  column; `CreateDashboardButtons` builds only the follow/pin toggle, there is no `_gear` field
  under `Cards/Tray/`, and `CapRole` has no entry for one.
- **the native short-rest widget does not dock on the board.** `TrayControlDockSurface` hardcodes
  `ShortRestDocked => false`, so the mod's own left-hand pad is the only short-rest control.
- **the active matrix's own collision note is stale on all three of its inputs.**
  `PlayTray.1.Core.cs:1009-1014` works out where that zone lands and concludes it leaves *"a clear
  gap past the pile column"*. It puts the mount at `0.34 + 0.17 = 0.51` (`ActiveMountBase` is
  `0.502`), it uses `ActivePileViewer.CardScale` ~ `0.82` (`Defaults.Cards.cs:534` ships
  `ActiveCardScale_Oak = 1f`), and it lays the cards *"at mount-local x = 0"* (`Columns` has been
  `3` since `85bbb8ca`). Redone with the shipped numbers, a full three-wide row spans board-local
  **0.40294 .. 0.60106** against the pile slabs' right edge at **0.40170**: the clearance is
  **1.2 mm**, not the ~76 mm the comment's arithmetic implies. The conclusion survives; the margin
  does not, and the diagram had to be laid out around that.

**On the file size, so nobody tries to "fix" it.** `board-en.png` is 256 kB and `board-de.png` is
268 kB, against the controls diagram's 81 / 87 kB. **The palette is not the cause and lowering it does
nothing**: 200, 160 and 128 colours, FASTOCTREE and MEDIANCUT, all land within 1 kB of each other,
because the cost is the OAK GRAIN -- spatial noise a PNG's row filters cannot predict -- and not the
colour count. The controls artwork is flat plastic and quantises to almost nothing; this one cannot.
It is in the same league as `styles-boards.png` (354 kB), which is the same wood.

**Where the callout room came from, and why it is not width.** The x range of the picture is set by
the content: the objectives column mounts at board-local -0.592 and the active-card matrix reaches
+0.559, so there is no horizontal gutter to put a label in, and widening the window shrinks the
board -- which is the subject. So the room is bought in **y**, where it is free: a band above the
initiative dock and a band below the grab rod. Three markers live inside the board's silhouette and are joined
to their names by leaders. The two named from ABOVE drop through a **gap between two tiles** of the
initiative dock rather than across a tile face -- board-local 0.000 is the dock's own centre seam and
0.20557 is the seam between its fifth and sixth tiles -- and the script asserts both, because that
alignment is arithmetic on the tile pitch and would break silently if the dock changed. The three
keys are named from BELOW instead, because the round readout sits directly over the top key and
covers the whole key column in x: there is no lane from above that reaches a key without crossing
it.

The German guard rail is three checks, not one, because German fails these in three different ways.
A word WIDER than its gap survives wrapping and paints over its neighbour -- that is the width
check. A string that is merely long does not overrun at all: it grows DOWNWARD, into the card fan or
off the bottom of the picture, which no width check can see -- so every callout also declares how
many lines its gap can absorb, and three of them declare `rows=1` for exactly that reason. And a
label that passes **both** of those can still be printed somewhere wrong, because a budget describes
a gap and says nothing about where the resulting block lands -- so the preflight also **measures
every block, in both languages, in canvas pixels**, and refuses if one leaves the picture, lies on
the board's own silhouette, lies on a ghost plate that is not its own, or overlaps another block.
The leader ends are checked the same way: each one has to land **inside the part it names**, with
3 mm of tolerance for the two that deliberately touch a rim.

All of it runs as a `check_callouts()` **preflight over every language before a single file is
written**, because the two languages are built one after the other and a failure found while drawing
German would otherwise have left a new English file on disk beside a stale German one. The canvas
frame (`W`, `MARGIN`, `TOP`, `PPM`, `X()`, `Y()`) is module level for the same reason: a preflight
measuring its own copy of the frame can pass while the picture is wrong.

**Half of the picture is drawn, and that is the honest part.** The initiative track, the objectives
panel, the element chips and the three card stacks are the game's own converted canvases docked
around the tray -- no render of the mod's asset can contain them. The script draws them as light
ghost plates at the mounts the code gives them (`PlayTray.3.Pose.cs`'s `*MountBase` constants), at a
lighter weight than the markers on the board itself, so the board stays the subject.

**The active-card matrix, and the four zones still left off.** Five parts were left out when this
diagram was built, to keep it readable. The user asked for one of them by name on 2026-09-06 --
*"Beim Board in der README fehlt im Bild das Areal wo die aktiven Karten angegeben sind"* -- so the
**active-card matrix** is now drawn, off the right edge past the pile stacks, sharing RED with them
because the picture groups by ZONE and not by function (the same way GOLD covers the objectives
panel and the element chips, and PURPLE the initiative track and the round readout). A ninth hue
beside the eight already there would be told apart by nobody. Still deliberately **not** drawn: the
**decision drawer**, the **item-use berth**, the **pin toggle** and the **pick-progress placard**. A
crowded diagram is the failure this design was fighting; adding one of the four is a decision, not
a chore.

**The matrix is the one part drawn at a scale rather than at its size**, and the script says so out
loud. Everything that tells a reader where it is and how it works is the shipped number -- the mount
at 0.502, three cards to a row, the 1.06 column gap, the 0.70 row overlap -- but the card SIZE is
halved. At full size a three-wide row starts 1.2 mm past the pile slabs, and this picture draws
those slabs as a plate roughly 1.7x their real width, so a to-scale matrix would be drawn straddling
that plate and would read as a mistake in the drawing rather than as the tight fit it is.

### `install-tree-{en,de}.png` -- "did it land in the right place?"

The install guide drew this tree in a fenced code block. A fence is monochrome, so the reader has to
*read* nine lines to find the two that decide whether the install worked. Here those two folders are
the only coloured things in the picture, and a reader who looks at nothing else still checks the
right two. Everything is drawn -- including the tick, because Inter has no U+2714 and a missing
glyph renders as a tofu box on exactly the row that matters.

### Not in this directory: the option picker's tiles

`src/GloomhavenVR/WorldUI/Options/VariantTiles/*.png` are a different set with a different job --
they ship inside the plugin DLL, not here. `tile_env_default.png` was rebuilt in the same round; its
recipe is `unity/asset-preview/build_variant_tile_default.py` and its provenance is
`.planning/variant-tiles.md`.

## Shared-window marker

`net-shared.svg` embeds the original `src/GloomhavenVR/Assets/net_shared.png` glyph and applies
`GrabbableModal.BadgeTint` at the bright end of its blue pulse. The SVG frames the visible glyph
instead of the tall source canvas; at 22 × 16 pixels it sits within a normal text line. The original
artwork and alpha remain unchanged. No replacement symbol is drawn.

Regenerate with `python3 docs/img/build-shared-marker.py` when the runtime asset or tint changes.
The marker identifies shared windows at the top right, below the close button when present.

# README images

The wordmark the README shows at the top is **not** here — it is the one the mod itself puts in
the main menu, referenced straight from `src/GloomhavenVR/Assets/GloomhavenVR_logo.png` so the
page and the game can never show two different logos.

## The demo clips are MP4, not GIF, and that was measured

The README is the project's pitch and these six clips carry it, so they matter more than any
paragraph on the page. They were GIFs for exactly one day. The two that exist were captured as
15-second 1280x720 clips and encoded both ways:

| | card-fan | figure-grab | together |
|---|---:|---:|---:|
| GIF, as first produced | 33.4 MB | 38.2 MB | **71.6 MB** |
| GIF, re-encoded hard (15 fps, 720 px, 128 colours) | 14.7 MB | 17.4 MB | 32.1 MB |
| WebM VP9 | 1.17 MB | 1.22 MB | 2.4 MB |
| **H.264 MP4, 960 px, 30 fps, CRF 26** | **1.17 MB** | **1.33 MB** | **2.5 MB** |

Roughly **29x smaller than the GIFs at twice the frame rate and full colour**, because a GIF has
256 colours, no interframe prediction and no chroma subsampling, and a dark scene with fine
particles is the worst case for all three. The optimised GIF is still 32 MB *and* looks worse.
This is not a close call; do not re-add a GIF.

**Git LFS is not the answer either, and the reason is worth writing down**: a plain `git clone`
of an LFS repo still downloads the CURRENT version of every LFS file — the smudge filter is the
default. LFS saves HISTORY, not the working copy, so for a clip committed once it saves nothing.
It would also spend the account's 1 GB/month LFS bandwidth on every clone.

## Encoding a new one

```
ffmpeg -ss <start> -to <end> -i <capture>.mp4 -an \
       -c:v libx264 -crf 26 -preset veryslow -pix_fmt yuv420p \
       -vf "scale=960:-2,fps=30" -movflags +faststart docs/img/<name>.mp4
```

- **`-an`** — the clips are silent on purpose. A README video is watched muted, and one that could
  suddenly play sound is worse than one that cannot.
- **`-pix_fmt yuv420p`** — without it Safari and several Android browsers will not decode it.
- **`-movflags +faststart`** — puts the index at the front so the video starts on a partial load.
- **Trim the capture.** A Virtual Desktop recording opens and closes on the VD dashboard and shows
  the controller models before the hands take over; both ends have to go. Find the cut by eye:
  `ffmpeg -i <capture>.mp4 -vf "fps=4,scale=400:-1,tile=5x4" sheet.png` and read the sheet.
- Keep each clip **under ~3 MB** and around **10-15 seconds**, one idea per clip, cropped to the
  action. Capture ONE eye — a stereo capture is twice the pixels for no benefit on a flat page.

## How they are embedded

GitHub does not reliably rewrite a relative path inside a `<video>` tag, so the src is the absolute
raw URL and a plain link sits inside the tag as the fallback if the tag is ever stripped:

```html
<div align="center">
  <video src="https://github.com/McFredward/GloomhavenVR/raw/main/docs/img/card-fan.mp4"
         width="800" controls muted loop playsinline>
    <a href="./docs/img/card-fan.mp4">card-fan.mp4</a>
  </video>
</div>
```

**While the repository is private that URL needs authentication**, so the player renders empty for
anyone not signed in — including in a preview shown to someone else. It resolves itself the moment
the repository is public, which is the state the pitch is written for. If a public-looking preview
is ever needed sooner, the fallback is a poster PNG that links to the file.

## Still missing

Each one has a visible placeholder in the README at the spot it belongs; dropping the file in with
the exact name below makes its `<!-- VIDEO: ... -->` comment the line to replace.

| File | What it should show |
|---|---|
| `overview.mp4` | The hero shot: standing at the table in a lit scenario, then dragging, rotating and zooming the board with the two-handed world grab. This is the one that has to sell it in three seconds. |
| `windows.mp4` | A window opens in front of the player, is grabbed by its bar, moved and resized, then reeled closer with the thumbstick. |
| `map-room.mp4` | The 3D campaign map room: pressing a table-rim cap, pointing at a location, the party token walking its route. |
| `environments.mp4` | The cellar and the night forest — firelight, the night sky, foliage moving, switching environments in the settings. |

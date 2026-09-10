# Video shot list

Optional additions to the playing guide. The READMEs already contain six clips each;
the playing guides link to those clips and use labelled stills. There are no empty video
placeholders in the published guides. Missing recordings here do not leave broken media.

## Additional recordings

| File | Approximate length | Destination | Show |
|---|---:|---|---|
| `tutorial.mp4` | 12 s | Playing guide, controls | Controller models and highlighted controls during the tutorial |
| `windows.mp4` | 12 s | Playing guide, windows | Grab, move, resize, reel with the stick and close a window; include the blue shared-window marker |
| `grab-rod.mp4` | 8–10 s | Playing guide, windows, beside the window clip | Move the control board by its rod, directly and with the laser |
| `environments.mp4` | 15 s | Playing guide, environments | Cellar and forest, element reactions, environment selection |

When a recording is supplied, add the markup to both language guides. Keep each clip in
one guide pair and link to it from other pages. Do not duplicate README footage in the
playing guide. The environment stills can remain until suitable footage is available.

## Presentation

The existing media sizes follow the maintainer's 2026-09-07 review:

| Width | Use |
|---:|---|
| 820 px | Labelled diagrams that must be read: controllers, board and installation tree |
| 720 px | Standalone video or wide image |
| 350 px | A still in a side-by-side pair |

For paired clips, use a two-cell table with `width="50%"` on each cell, as in the README.
Page ornaments use `divider.png` at 600 px and `divider-small.png` at 340 px where the page
already uses section breaks. The shorter playing guide does not use those ornaments.

Upload finished clips through a GitHub issue or comment box and use the resulting
`user-attachments` URL; do not commit MP4 files. Uploading or posting is a maintainer action.
A standalone clip uses:

```html
<p align="center">
  <video src="https://github.com/user-attachments/assets/UUID" width="720" controls muted loop></video>
</p>
```

Keep each clip silent and focused on one interaction. Trim headset/streaming dashboards
from both ends and show one eye. Encoding instructions and the existing media provenance
are in [img/README.md](img/README.md). After editing media, check both language pages and
run `python3 scripts/check-docs-i18n.py`; that checker cannot judge the footage itself.

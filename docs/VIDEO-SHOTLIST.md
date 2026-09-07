# Video shot list — the clips that still have to be recorded

**This is the only list.** Everything the docs are waiting for is one row below, with the filename
it has to be saved as and the spot that is already prepared for it. Record one, drop it in, delete
two comment markers — nothing else in the repo has to be hunted through.

Every prepared spot **announces itself on the rendered page** — a bordered box saying which clip is
missing, what it has to show, and exactly what to replace to fill it. That is a deliberate change
from 2026-09-07: the spots used to be HTML comments, which render as nothing at all, and the person
who has to shoot the clips reported the obvious consequence — *"die sehe ich in der README nicht"*.
An invisible reminder is not a reminder. The finished markup still sits in a comment directly under
each box, so nothing is broken while a clip is missing; the box above it is what you can see.

Find them all with

```
grep -rn "VIDEO PLACEHOLDER" README.md README.de.md docs/PLAYING.md docs/PLAYING.de.md
```

— six per language, twelve in all, and each one repeats in place what that clip has to show.

## What still has to be recorded

**Delivered on 2026-09-07 and no longer on this list:** `multiplayer.mp4` (slot 2, README and
playing guide in both languages), plus two clips that were not on it — the scenario board and the
campaign map room. All three are served from the attachment CDN, so the playing guide takes the
same `<video>` markup as the README rather than a committed poster: an mp4 that already plays
inline is worth more there than a thumbnail, and it saves the repository three binaries.
The four videos in *Your cards and your board* and *The board in front of you* were shot on
2026-08-24 against ModBuild 248; the board pair has been replaced by the fresher clip and the
card pair is now the oldest footage on the page.

| # | File | ~Length | Prepared spot | What it shows | What the viewer knows afterwards |
|---|---|---:|---|---|---|
| 1 | `overview.mp4` | 10-12 s | `README.md` + `README.de.md`, straight under the nav row, above *Your cards and your board* | Standing at the table in a lit scenario, then dragging, rotating and zooming the whole board with the two-handed world grab. | What this mod **is**, in three seconds: a real table you stand at, and a board you take hold of. |
| 3 | `tutorial.mp4` | 12 s | `docs/PLAYING*.md` ▸ *The controls*, under "You do not have to learn this" | Your hands turning into the controller you are actually holding, and one key after another lighting up as each control becomes useful. | The fourteen bindings on the controls picture are **taught, not memorised**. |
| 4 | `windows.mp4` | 12 s | `docs/PLAYING*.md` ▸ *Windows* (left of the pair) | A game window grabbed by its bar, moved, resized, reeled closer and further with the stick while the laser holds it, then closed with its X. | The game's windows are **furniture you place once**, not a UI that reappears where it likes. |
| 5 | `grab-rod.mp4` | 8-10 s | `docs/PLAYING*.md` ▸ *Windows* (right of the pair) | The control board taken by the rod under it and carried to a new place — once with the hand, once with the laser from across the table. | **Everything in the room hangs from a rod**, and the same grab moves all of it. |
| 6 | `environments.mp4` | 15 s | `docs/PLAYING*.md` ▸ *The room you play in*, under the two stills | Firelight and drips in the cellar, moonbeams and moving foliage in the forest, an element infusion fading the room over, and the environment switched in the settings. | The rooms are **alive and react to the scenario** — which the three stills cannot show at all. |

Clips 4 and 5 are one pair under one heading, on the same principle as the two README clusters: one
idea (*it hangs from a rod, you take hold of it*), shown twice.

**Clip 2 is recorded once and used twice.** The README spot takes an attachment URL, the playing
guide takes a poster; the mp4 is the same file.

## Every spot is a visible box; what goes IN it differs by page

Each prepared spot is the same two things stacked: a `<table>` box that renders on the page and says
what is missing, and an HTML comment under it holding the finished markup. Filling a spot is always
the same three moves — put the file where the markup expects it, delete the box, delete the two
comment markers. What the markup is differs by page:

| Document | Markup in the comment | Why |
|---|---|---|
| `README.md`, `README.de.md` | `<video src="https://github.com/user-attachments/assets/…">` | a video only ever **plays** in a GitHub README when it is served from the attachment CDN. Upload the mp4 through a comment box to get the URL — `docs/img/README.md` has the full procedure, including why nothing you commit will play by itself. |
| `docs/PLAYING.md`, `docs/PLAYING.de.md` | poster jpg linking to the committed mp4 | the guide is read, not pitched; a thumbnail row costs no page height and works on a clone with no network. |

So a README clip needs the mp4 **committed and uploaded**; a playing-guide clip needs the mp4 and a
`<name>-poster.jpg` beside it in `docs/img/`. Clip 2 needs all three.

## Recording, cutting, encoding

All of it — the ffmpeg command line, the trim (a Virtual Desktop capture opens and closes on the VD
dashboard and shows the controller models before the hands take over, and both ends have to go), the
size and length budget, the one-eye rule, and how to make a poster — is in
[`img/README.md`](img/README.md) under *Encoding a new clip*. Do not re-derive it.

Two things that are decided and not worth re-opening: the clips are **silent** (`-an`), and each one
carries **one idea**, cropped to the action.

## Housekeeping

- **Both language pages, always.** A placeholder filled on one side and not the other is exactly the
  drift `scripts/check-docs-i18n.py` exists to catch — though it counts headings, and a placeholder
  box is deliberately not a heading, so this one is on you. Run it after any doc change regardless.
- **Keep the box visible until the clip is in.** Do not collapse one into a `<details>` and do not
  move it back inside a comment. The whole point of the 2026-09-07 change is that an empty spot is
  something you can see on the page.
- **Record something that is not on this list?** Add a row, and add its placeholder to both language
  pages, rather than starting a second list somewhere else.
- `docs/img/README.md` points at this page for what is still missing and carries the encoding
  recipe. It is not a second checklist: this page is the checklist.

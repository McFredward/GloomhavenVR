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

— **three per language, six in all, and every one of them is in the playing guide.** The README has
held no placeholder since 2026-09-07. Each box repeats in place what that clip has to show.

## How big — THREE WIDTHS, AND NOTHING ELSE

**Every picture and every clip in every document carries an explicit width, and it is one of three.**
Maintainer's ruling, 2026-09-07: *"Mach die zwei ersten Videos kleiner und nebeneinander. Die
verbrauchen zu viel Platz. Generell überdenke in allen Docs nochmal die Größe der Medien. Es soll gut
und übersichtlich aussehen."*

The defect was not that the numbers were wrong. It was that **not one `<video>` had a width at all** —
a clip with no width renders at the full content column (~830 px), so six of them in a row was most
of the page. The stills each had a width and they were all different: 345, 420, 680, 700, 720, 820,
860. Two of those (860) were wider than the column and were being scaled down by GitHub anyway.

| Width | For | Where |
|---:|---|---|
| **820** | a labelled reference picture that has to be READ | the controls picture, the board diagram, the install tree |
| **720** | a single clip, or a wide still | every standalone video; `promo.gif`; `styles.png`; the element comparison |
| **350** | one half of a side-by-side pair | the opening pair; the two environment stills; the windows/grab-rod pair |

Two at 350 plus the table gutter come to about 720, so a pair and a single occupy the same block of
page. That is the whole point of the scale: **nothing should look like it was sized by whoever added
it.**

A pair is a two-cell table, not two `<video width>` tags — the cells carry `width="50%"` and the
clips fill them. That markup is the one this repository has used for pairs since 2026-08-24 and it
survives GitHub's HTML sanitiser; a `width` attribute on `<video>` is used only for singles, where
losing it to the sanitiser would merely restore the old full-width behaviour rather than break the
layout.

## Which document gets which clip — ONE CLIP, ONE PAGE

**No clip appears in two documents.** Maintainer's ruling, 2026-09-07: *"Ich will das die Videos
eigentlich nicht auf verschiedenen docs doppelt auftauchen."* Before it, every clip in the playing
guide was a second copy of a README clip and one of them had a third copy in INSTALL. The split
below is by what each document is FOR, and a new clip belongs to exactly one of them:

| Document | What it is | What it shows |
|---|---|---|
| `README.md` / `README.de.md` | the pitch — someone deciding whether to install | six clips. THE ORDER IS DELIBERATE and was set by the maintainer on 2026-09-07: the overview and the FIGURE LIFT together at the very top ("ziemlich präsent"), then the feature list, then MULTIPLAYER — moved up from seventh place because "der Multiplayer ist einer der größten Features" — and only then the control board, the scenario board and the map room |
| `docs/PLAYING.md` / `.de.md` | the manual — someone who has installed and wants to learn | only INSTRUCTIONAL clips, which by their nature exist nowhere else: the controls tutorial, windows, the grab rod, the environments. Plus its own labelled pictures, `controls-*.png` and `board-*.png`, which are the manual's real content and have no README twin |
| `INSTALL.md` / `.de.md` | the steps | no clips at all. It has `install-tree-*.png`, which is the only picture the task needs |

A guide section whose subject IS shown on the front page carries a one-line pointer to it (`› …`)
instead of the clip. That keeps the reader routed without paying for the same video twice.

**No clip is a GIF any more.** `figure-lift.gif` was a stand-in for footage that had not been
uploaded; when it was, the GIF went. If a moving image is ever needed again before an upload, the
encoding is recorded in `img/README.md` — but the upload is the answer.

**The two environment stills are the one duplication left standing**, and deliberately: `env-cellar`
and `env-forest` are in both the README and the guide, because the guide's *The room you play in*
would otherwise be a heading with nothing under it until `environments.mp4` lands. When that clip
arrives, drop the stills from the guide.

## What still has to be recorded

**Delivered on 2026-09-07 and no longer on this list:** `multiplayer.mp4` (slot 2, README and
playing guide in both languages), plus three clips that were never on it — the scenario board, the
campaign map room, and the control board. All are served from the attachment CDN, so the playing
guide takes the same `<video>` markup as the README rather than a committed poster: an mp4 that
already plays inline is worth more there than a thumbnail, and it saves the repository several
binaries.

**Slot 1 was filled the same day, so THE README HOLDS NO PLACEHOLDER AT ALL.** `overview.mp4` is
in, and with it the last of the four 2026-08-24 / ModBuild 248 clips left the page: the board pair
went first, and the *Your cards and your board* pair — the oldest footage on the page, 224 builds
old — was replaced by a single `controll-board.mp4` that shows the fan, a card being read, cards
laid into the recesses and the active column, which is everything the pair showed and more. The
campaign-map clip shot earlier that day was itself superseded within hours by a longer take that
opens a quest and a story panel.

Every clip in the README was cut against the same defect: all three source recordings carried the
Virtual Desktop overlay — the Quest status bar and the app tiles — at the head, the tail, or both.
The cut points are the frames where it clears, verified frame by frame, and the audio track is
removed outright rather than muted.

**The four rows left below are all in `docs/PLAYING*.md`.** Nothing in the README is waiting on
anything: the figure lift was the last thing it needed and it landed the same evening, as an
attachment clip directly under the overview.

| # | File | ~Length | Prepared spot | What it shows | What the viewer knows afterwards |
|---|---|---:|---|---|---|
| 3 | `tutorial.mp4` | 12 s | `docs/PLAYING*.md` ▸ *The controls*, under "You do not have to learn this" | Your hands turning into the controller you are actually holding, and one key after another lighting up as each control becomes useful. | The fourteen bindings on the controls picture are **taught, not memorised**. |
| 4 | `windows.mp4` | 12 s | `docs/PLAYING*.md` ▸ *Windows* (left of the pair) | A game window grabbed by its bar, moved, resized, reeled closer and further with the stick while the laser holds it, then closed with its X. | The game's windows are **furniture you place once**, not a UI that reappears where it likes. |
| 5 | `grab-rod.mp4` | 8-10 s | `docs/PLAYING*.md` ▸ *Windows* (right of the pair) | The control board taken by the rod under it and carried to a new place — once with the hand, once with the laser from across the table. | **Everything in the room hangs from a rod**, and the same grab moves all of it. |
| 6 | `environments.mp4` | 15 s | `docs/PLAYING*.md` ▸ *The room you play in*, under the two stills | Firelight and drips in the cellar, moonbeams and moving foliage in the forest, an element infusion fading the room over, and the environment switched in the settings. | The rooms are **alive and react to the scenario** — which the three stills cannot show at all. |

Clips 4 and 5 are one pair under one heading, on the same principle as the two README clusters: one
idea (*it hangs from a rod, you take hold of it*), shown twice.

**Clip 2 was recorded once and used twice**, and it settled the question for every clip after it:
**BOTH pages take the `<video>` from the attachment URL. There is no second route.** The poster
fallback that used to be described here was removed on 2026-09-07 along with every committed mp4.

## Every spot is a visible box, and every spot takes the same markup

Each prepared spot is the same two things stacked: a `<table>` box that renders on the page and says
what is missing, and an HTML comment under it holding the finished markup. Filling a spot is three
moves — upload the mp4, paste the URL over `PASTE_UUID_HERE`, delete the box and the two comment
markers.

**IT USED TO DIFFER BY PAGE AND IT NO LONGER DOES.** The README took an attachment `<video>` and the
playing guide took a poster jpg linking to a committed mp4. That second route was deleted on
2026-09-07, with every file it depended on, because it never worked the way it promised: **a video
served from a repository path does not play on github.com** — a poster link only offers the reader a
download. Maintainer's ruling, verbatim: *"Die Videos müssen von mir in einem Issue hochgeladen
werden und ich gebe sie dir dann."*

So, for every spot on every page:

    <p align="center">
      <video src="https://github.com/user-attachments/assets/<uuid>" controls muted loop></video>
    </p>

**Nothing is committed.** Upload the mp4 through a GitHub issue or comment box — `docs/img/README.md`
has the procedure — and hand over the `user-attachments` link. Do not add the file to `docs/img/`;
there is no longer any code that would poster it and no page that would play it.

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

# Video shot list — the clips that still have to be recorded

**This is the only list.** Everything the docs are waiting for is one row below, with the filename
it has to be saved as and the section it belongs in.

> **THERE ARE NO PLACEHOLDER BOXES IN THE TREE (checked 2026-09-08 at ModBuild 483).** This page
> used to describe a bordered "VIDEO PLACEHOLDER" box rendered at each waiting spot, and told you to
> find them with
> `grep -rn "VIDEO PLACEHOLDER" README.md README.de.md docs/PLAYING.md docs/PLAYING.de.md` —
> "three per language, six in all". That grep returns **nothing**: not one marker survives, and
> `docs/PLAYING.md` and `docs/PLAYING.de.md` carry **zero `<video>` tags** between them. The four
> rows in *What still has to be recorded* therefore have **no prepared spot** — their "Prepared
> spot" column names where a clip WOULD go, not something that exists. Whoever fills one writes the
> markup from scratch, from the block under *Every spot takes the same markup* below.
>
> The 2026-09-07 reasoning for making an empty spot visible on the page still stands
> (*"die sehe ich in der README nicht"* — an invisible reminder is not a reminder). If you re-add
> boxes, re-add the grep token with them and correct this paragraph; if you decide the guide reads
> better without them, delete the *Prepared spot* column instead of leaving it describing a
> mechanism the tree does not have.

## The ornament — TWO assets, and which one goes where

**The first break on a page is the PAGE's; every later one is a SECTION's.** Maintainer's ruling,
2026-09-07: *"Der Trenner für ganz oben gefällt mir. Alle weiteren Trenner sollten etwas kleiner
sein."*

| | file | page width | where |
|---|---|---:|---|
| the rule | `docs/img/divider.png` — 1200x122 | **600 px** | ONCE per document, directly under the header block. It closes the title and opens the document |
| the mark | `docs/img/divider-small.png` — 680x94 | **340 px** | every break after that |

600 px is deliberately narrower than the 720 the media uses, because a break is not a full-bleed
element; 340 is half of it.

**THE SMALL ONE IS A SECOND ASSET, NOT A SECOND WIDTH, and that is the whole point.** Rendering the
rule at 340 keeps every bit of its structure — the double line, the six diamonds, the star inside
the hexagon — and merely makes it too small to read. The mark instead DROPS the structure and keeps
the hexagon, so the two read as the same ornament at two volumes. Measured at page size before it
shipped: 8.7 % ink coverage where the discarded candidates ran to 19.8 %, with a 2.1 px stroke at
full alpha — quiet, and still alive on the dark theme. If a third one is ever needed, size it by
INK, not by box: a hairline that averages below ~1.5 px at page size is a ghost, and it dies on the
dark theme first.

**It is transparent and mid-tone antique gold on purpose.** GitHub renders these pages on a light
AND a dark background and the reader picks; anything white, cream, black or near-black would vanish
on one of the two. Every candidate was composited onto `#ffffff` and `#0d1117` before one was
chosen, and the shipped asset was checked the same way. Do that again for any new ornament.

Three candidates were generated for the rule — a hex-and-star motif, a leafy scrollwork vine, and
a minimal tapered rule. The hex won for reasons worth keeping: it is on-theme for a hex-grid board game, it
still reads when shrunk to a 40 px band, and it is distinctive. The vine was prettier and generic;
the minimal rule was indistinguishable from an `<hr>` at page size.

Three more were generated for the mark, each judged UNDER the rule at 600 px rather than alone,
because the question is whether it reads as the same ornament one step quieter. A row of three
hexagons arrived with scrollwork wings — that is MORE structure, not less. A solid lozenge read
well but at 19.8 % coverage was no quieter than what it replaced. The bare hexagon on a hairline
won on the measurement above.

| Document | Ornaments | Why |
|---|---:|---|
Counted in the tree on 2026-09-08; both language twins of each document agree, which is the
property that matters most here.

| `README*.md` | 8 — **1 rule + 7 marks** | the pitch. It should read as chapters, not as a list. Roughly one per screenful on a page carrying six clips |
| `docs/PLAYING*.md` | 7 — at its existing rules only, never above headings; **1 rule + 6 marks** | the manual is SCANNED; an ornament before every heading would slow the scan |
| `INSTALL*.md` | 10 — **1 rule + 9 marks** | a task page whose breaks separate NUMBERED steps. **This row used to say "1 — the rule, no marks", and argued that the steps must keep plain rules so the ornament could not compete with the numbers.** The tree does not do that and has not for some time: every break on the page is a mark. Nobody has recorded which way was decided, so treat neither the old prose nor the current markup as a ruling — if the numbers really do read better without ornaments, change the page and this row together |

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

**The two environment stills are no longer duplicated** — this paragraph used to say they were the
one duplication left standing, in both the README and the guide. At 2026-09-08 `env-cellar.jpg` and
`env-forest.jpg` appear only in `docs/PLAYING*.md`, under the heading **`## Environments`** (which
is what that section is called; several rows below still name it *The room you play in*). They are
holding the section open until `environments.mp4` lands; drop them when it does.

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

**The four rows left below all belong in `docs/PLAYING*.md`.** Nothing in the README is waiting on
anything: the figure lift was the last thing it needed and it landed the same evening, as an
attachment clip directly under the overview. Confirmed 2026-09-08 — the README carries six
`<video>` tags and no placeholder, and the playing guide carries none of either, so the *Prepared
spot* column below is a destination rather than a slot (see the note at the top of this page).

| # | File | ~Length | Prepared spot | What it shows | What the viewer knows afterwards |
|---|---|---:|---|---|---|
| 3 | `tutorial.mp4` | 12 s | `docs/PLAYING*.md` ▸ `## The controls`, under "You do not have to learn this" | Your hands turning into the controller you are actually holding, and one key after another lighting up as each control becomes useful. | The fourteen bindings on the controls picture are **taught, not memorised**. |
| 4 | `windows.mp4` | 12 s | `docs/PLAYING*.md` ▸ `## Windows` (left of the pair, once one exists) | A game window grabbed by its bar, moved, resized, reeled closer and further with the stick while the laser holds it, then closed with its X. | The game's windows are **furniture you place once**, not a UI that reappears where it likes. |
| 5 | `grab-rod.mp4` | 8-10 s | `docs/PLAYING*.md` ▸ `## Windows` (right of the pair, once one exists) | The control board taken by the rod under it and carried to a new place — once with the hand, once with the laser from across the table. | **Everything in the room hangs from a rod**, and the same grab moves all of it. |
| 6 | `environments.mp4` | 15 s | `docs/PLAYING*.md` ▸ `## Environments`, under the two stills | Firelight and drips in the cellar, moonbeams and moving foliage in the forest, an element infusion fading the room over, and the environment switched in the settings. | The rooms are **alive and react to the scenario** — which the three stills cannot show at all. |

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

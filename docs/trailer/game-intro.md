# Approved campaign intro

The maintainer approved the first 46 seconds of the cinematic trailer and asked
to use that material for the game's first-save intro on 2026-10-16. The native
`UIMapFTUEInitialStep` selects `CP_Intro/GH_CP_Intro`; its PC movie is 48.85 seconds
long, including its final black tail.

The replacement is a mod-local H264/AAC MOV at 1920×1080, 60 fps, 2931 frames. Game
StreamingAssets remain read-only. The movie ships beside the plugin as
`Movies/CP_Intro/GH_CP_Intro.mov`. Native and cosmetic multiplayer decoders use
the same canonical movie key. The native player still owns volume, skip,
completion, errors and continuation. A missing optional movie falls back to
the original game file; release/developer installers include the new asset.

The maintainer's 2026-10-17 stereo instruction applies to **this already replaced
GloomhavenVR intro**, not the original game's intro or a new trailer edit. The
stereo derivative uses the exact approved MOV identified below. Its title,
notice removal, cuts, speed and soundtrack remain authoritative. Only this intro
is being converted; other films wait for the maintainer's intro headset result.
See [the stereo conversion contract](../stereo-video-intro.md).

## Picture and sound

The native intro begins with a stationary Gloomhaven title rather than the
trailer PEGI card. Its long title occupies the same interval as the approved
O01 shot once the common picture sequence is aligned. Keeping that full title
interval would conceal the approved opening. The replacement therefore puts
the exact existing GloomhavenVR artwork across the three-second opening card, retaining
all approved opening shots, their order and original speed. The later fire
title already carries the approved VR suffix.

Frames 180..2745 retain the approved v11 trailer at their original addresses.
They include all three opening AI shots O01/E01/E02. The final native fade
uses game frames 2626..2810 at output frames 2746..2930; the common native picture
sequence is two seconds earlier than the trailer. Its black tail is shortened
to retain the exact 48.85-second completion clock. No next
combat shot, "Merciless Tactical Combat" title or Flat explanation is included.

The approved soundtrack and existing cloned name patch are retained until 45.58s.
A 200ms crossfade brings in the original game's final sting, aligned by the
measured 2.012-second decoded-audio offset. Only that native tail has 0.60 gain
because its source peak exceeds full scale. There are no new voice, model or
ASR calls. At the maintainer's explicit 2026-10-17 request, Build 679 removes
the on-picture unofficial/fan-made notice from this game intro. The full trailer
retains its existing notice.

The body notice occupied the original lower letterbox. The renderer restores
actual normalized native-source pixels only within x1520/y1008/w384/h64;
all 2566 source regions were checked to contain only that original black bar.
The title and native ending omit their previous text overlay. No scene content
is cropped or generated. All 2291 AAC packet payloads and timestamps are copied
exactly from the reviewed Build 678 intro.

## Provenance and verification

- Source movie: accepted v11 SHA256
  `6f4007b7db11ebffde14ea92e1585d0bc8f6bb43f48fb6e8d6ba3b21a1d127ee`.
- Native PC movie SHA256
  `589d421dc23d9733df8092f1042de527179ed9b52e37f18ae5bd50c8b49f4a20`.
- Delivered MOV SHA256
  `22fc9f48d4da6783fed379122e8c5675c4149afd9c0bc90550a9e4e22696fec5`.
- Generator: `scripts/trailer/build_game_intro.py`, with private input manifest,
  exact command/filter, render receipt and review under
  `.planning/debug/trailer/remake-v1/game-intro-v2/` (Build 679); the preceding
  Build 678 review remains under `game-intro-v1/`.

The initial two-second logo attempt was rejected during root and independent
review because it exposed a different-sized inherited title at 2s. The final
three-second card removes that jump without concealing O01. Rejected-attempt
receipts remain historical evidence, not approval of that composition.

The browser MP4 is a stream-copy remux of the MOV for convenient review. Root
and independent review checked the final title and seams; the independent
review also covered desktop/mobile playback and the published Debug download.
All 2566 retained trailer frames were compared at their original timestamps,
and the retained native ending and approved audio were checked separately.

Integrated playback checks pass: native-video has 93 assertions and 10 mutation
controls, native-playback has 466 assertions, and video-playback has 35 assertions
and five controls. They exercise the actual patched native caller, canonical
remote movie resolution and both installers. Debug and Release production
builds have zero errors and warnings; the packaged movie matches the reviewed
asset byte for byte.

Build 678's complete local run initially passed 218 of 221 suites. A missing logging
boundary in the unrelated native-atlas fixture was repaired without changing
production code or assertions. The three affected atlas/recovery/scenery suites
then passed serially with their original deadlines and negative controls. This
is composed coverage using the unchanged 218 passing suites, not a fresh
221-suite pass. All 16 source suites and 300,518 wire golden assertions pass.
Receipts, failed attempts and cleanup reports remain in the private review root.

Build 679 repeats the focused native-video suite: 93 assertions, ten controls,
actual packaging and both installer paths pass. Debug and Release builds have
zero errors/warnings. All 2931 new frames retain the preceding same-clock scenic
content within H264 encoding tolerance; the wider former footer region stays
black in every frame. Worker and root visually reviewed the new movie. Unchanged
runtime coverage above is inherited from Build 678, not claimed as a new full run.

These checks do not establish actual headset presentation or first-save/skip
behavior on the maintainer's hardware. Build 679's Debug update is available
from the review page for that hardware check.

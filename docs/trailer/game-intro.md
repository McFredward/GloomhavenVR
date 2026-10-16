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
ASR calls. The lower-right unofficial/fan-made label remains throughout.

## Provenance and verification

- Source movie: accepted v11 SHA256
  `6f4007b7db11ebffde14ea92e1585d0bc8f6bb43f48fb6e8d6ba3b21a1d127ee`.
- Native PC movie SHA256
  `589d421dc23d9733df8092f1042de527179ed9b52e37f18ae5bd50c8b49f4a20`.
- Delivered MOV SHA256
  `25db586688981fee9f2d508c2fdc0c51896159802a50b4c0115a6bb818803778`.
- Generator: `scripts/trailer/build_game_intro.py`, with private input manifest,
  exact command/filter, render receipt and review under
  `.planning/debug/trailer/remake-v1/game-intro-v1/`.

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

The complete local run initially passed 218 of 221 suites. A missing logging
boundary in the unrelated native-atlas fixture was repaired without changing
production code or assertions. The three affected atlas/recovery/scenery suites
then passed serially with their original deadlines and negative controls. This
is composed coverage using the unchanged 218 passing suites, not a fresh
221-suite pass. All 16 source suites and 300,518 wire golden assertions pass.
Receipts, failed attempts and cleanup reports remain in the private review root.

These checks do not establish actual headset presentation or first-save/skip
behavior on the maintainer's hardware. Build 678's Debug update is available
from the review page for that hardware check.

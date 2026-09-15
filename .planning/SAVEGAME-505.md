# Savegame introduction windows — build 505

## Evidence

The supplied local `LogOutput.log` and `Player.log` are from 2026-09-15,
ModBuild 504 / assembly 1.0.0. The remote files are from 2026-09-14, build 500;
they are not evidence for this test. No screenshot was supplied for these symptoms.

- `Player.log:3699–3709`: native `UIMapFTUEInitialStep` starts the campaign intro,
  hides the party display, registers its escapable and starts the movie decoder/audio.
  The video camera writes to the desktop after both head-camera eyes. Playback starts;
  the missing VR surface is the source-proven rendering gap.
- `LogOutput.log:217,304`: the hint producer is on `Quest UI` / `Campaign Canvas`,
  outside the corresponding floated screen. Transform ancestry cannot identify it.
- `LogOutput.log:347`: the introduction is adopted into `New Party display`, but its
  recorded home is `GloomhavenVR.Panel_Modal_LevelBoxMessageLayoutGroup`. Its previous
  standalone conversion was still alive. The grab bar and independent fit/capture
  machinery consequently survive moving their content to another window.
- `LogOutput.log:378,385`: the moved hint reaches 11741×7560 authored pixels;
  character-column ink grows to 1920px against its pinned 304px width. A parked
  overlay must not participate in the measurement that determines its own size.
- Native `UIIntroductionManager` retains individual queued MessageInfo objects.
  `UIIntroduceBase.isShown` describes a producer process, including later queued
  messages, and cannot reliably identify the current message's owner.
- Native `LevelMessageUILayout.Init` explicitly uses its root Image for
  `ShowScreenBG`. That fullscreen dimmer is not the message's text/controls envelope.
- The introduction group also incorrectly qualified for sticky map windows and a
  generic mod close X. Native continuation lives in its message callback, not Hide.

## Changes

Fullscreen native movies receive a dedicated world-space window, gated on a decoded
frame. Intro clicks use the original native skip path. Shared movie presentation uses
additive TLV 72, original local movie assets and the existing shared-window pose rules;
cosmetic decoders never invoke native completion callbacks or publish as new sources.
Multiple native copies elect one presentation source and retire its completed play
without replaying another copy's remaining tail. Video frames join the window draw
ordering, and decoded remote frames wait for their shared initial placement.

Introduction provenance is captured from native producer calls onto each queued
message, including asynchronous highlight/reward steps. Exact serialized UI references
identify owners whose introducers live elsewhere in the hierarchy. Global introductions
whose actual owner is hidden remain standalone; no unrelated screen is chosen or opened.

Before adoption, the old standalone conversion and chrome are retired synchronously.
The original native transform is restored before the composite records its home, and
the conversion pass rechecks the live hierarchy so it cannot recreate the old frame in
the same tick. Hint overlays do not resize their owning window. Existing layout flags
are restored exactly, and native closing fades retain their owner until they finish.
Introductions are not sticky and cannot be dismissed by a mod X that bypasses their
native continuation.

## Validation

All 17 source checkers, 253,674 wire assertions and the existing production suites
pass. New production-linked suites pass 39 native-video, 35 shared-playback and 20
hint assertions, with eight deliberate failing variants between them. The updater
suite also passes nine assertions and its negative control. Strict Release builds
with zero warnings/errors; bilingual docs and whitespace checks pass.

Compiled comparison against the retained build-502 baseline reports 26 changed
types and 12 additions, with no removals. Changes include the already-integrated
gold/updater fixes and version constants; remaining changes are video presentation,
shared playback and hint ownership/lifecycle. Config keys remain 625, patch
signatures increase 160→161, and log markers 4,719→4,723, with none removed. The
guard's nonzero result denotes these reviewed compiled differences, not a failing
source check or test. Automated checks do not prove headset rendering.

Hardware replay still needs: campaign intro from a save, the complete first-character
creation sequence, repeated hints on the same screen, another fullscreen movie, and
two-player video start/skip/finish plus shared grab/resize and peer departure.

Development remains version 1.0.1. Build 505 is DLL-only relative to the build 483
bundle; all VR peers must use 505. No main push, tag replacement or release is requested
in this round.

# Stereoscopic in-game video handoff

Date: 2026-10-17. Requested recipient thread: `01a148e9-d5c1-75e1-aada-1469991c8b50`.

## User direction and ownership

The maintainer asked for a possible plan to make all in-game videos 3D and
accepted the proposed direction. They then explicitly assigned this feature
to another agent and asked the trailer agent to hand over the plan when asked.
The trailer agent remains responsible for the full trailer and incoming real
VR capture replacement. No stereoscopic implementation, inference, paid API
call, stereo media asset or headset test has been performed for this feature.
The accepted direction is a plan, not evidence that any candidate model works
well on these assets or authorization for an unspecified new inference budget.

Read AGENTS.md, CLAUDE.md, STATE.md and the newest NetProtocol build notes before
implementation. Current dev was Build681 when this handoff was written; do not
reuse the historical Build679 number for a new distributed runtime build.
Coordinate tracked ownership with the other active agents. The trailer agent
currently owns trailer documentation and private `vr-gameplay-v3/` evidence,
not stereo runtime files. This document is the only stereo-planning file it
creates; it will not edit the recipient's implementation files.

## Intended experience

Stereoscopic cinema in the existing VR movie surface: distinct left/right eye
pictures with depth within the film, retaining the film's fixed camera motion.
Free-viewpoint/room-scale reconstruction is outside the proposed first scope.
Precompute the conversion offline. Runtime should decode and display the movie
without an inference service. Preserve accepted composition, duration, frame
addresses, source speed and audio. Protect readable text/logos and actual actor
appearance; inspect temporal depth, disoccluded edges and stereo comfort.

## Inventory already inspected

`ressources/GH_Data/StreamingAssets/Movies/` contains 19 unique external PC MOVs:

- `CP_Intro/GH_CP_Intro.mov`: 48.85s.
- `Heroes/*.mov`: 11 character unlock videos, roughly 12–17s each.
- `Ambient/*.mov`: seven menu backgrounds, mostly 18.75s; Crypt is 37.53s.

Total native duration: approximately 354.8s. All inspected PC MOVs are
1920x1080 at 60fps. `Movies_MP4_30/` and `Movies_MOV_30/` are platform variants,
not another 38 distinct film subjects. Embedded VideoClip/startup/promotion
paths have not been exhaustively inventoried; all live VideoPlayers still need
classification before claiming complete coverage. Shared game references are
read-only.

IMPORTANT: convert the CURRENT mod-local intro, not the native intro. The user
accepted the cinematic trailer opening as the first-save movie and subsequently
explicitly removed the on-picture fan-mod notice from this intro only.

- Current intro: `src/GloomhavenVR/Assets/Movies/CP_Intro/GH_CP_Intro.mov`.
- SHA256: `22fc9f48d4da6783fed379122e8c5675c4149afd9c0bc90550a9e4e22696fec5`.
- 2931 frames / 60fps / 48.85s, H264 + stereo48k AAC.
- Movie guide: `docs/trailer/game-intro.md`.
- Reproducible generator: `scripts/trailer/build_game_intro.py`.
- Private review/receipts: `.planning/debug/trailer/remake-v1/game-intro-v2/`.
- Browser: `http://192.168.0.80:8090/remake-v1/game-intro-v2/REVIEW.html`.
- Historical media feature commits: `f9b8ea349`, `ee48270de`; label-free
  generator `6d44bd3d6`, final Build679 asset `53331cefa`.

The full trailer retains its existing unofficial/fan-made mark. Do not alter
the trailer master or its footage during stereo runtime work.

## Existing playback and integration points

- `src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs`: mod-local movie
  resolver; allowlisted canonical keys `CP_Intro/GH_CP_Intro.mov` and
  `Heroes/<class>.mov`; the registered transpiler replaces the native URL setter
  before first Play. Native sound, skip, errors, callbacks and continuation remain
  authoritative. No restart/postfix decoder reconfiguration.
- `NativeVideoWindow.cs` in that directory: grabbable native/cosmetic movie
  window reading prepared VideoPlayer textures. Current RawImage is mono.
  Stereo packing needs the per-eye display aspect, not the whole packed width.
- `src/GloomhavenVR/Net/Remote/RemoteVideoPlayback.cs`: existing owner clock and
  cosmetic peer decoder; keep canonical asset keys and native progression rules.
- `FlatScreenStereo.1.State.cs`, `.2.Compositor.cs`, `.4.PerEye.cs`: captured
  menu/background presentation. Current movie depth shift moves a flat movie
  plane behind the UI; it does not reconstruct within-film depth. Hardware notes
  warn that changing an already-playing native player's renderMode/targetTexture
  caused black movies. Review these contracts before touching that path.
- `decompiled/GH.Runtime/VideoCamera.cs`: actual intro/character playback.
- `decompiled/GH.Runtime/BackgroundView.cs`, `SceneController.cs`: separate
  ambient selection/playback. The intro URL patch alone cannot cover these.
- `decompiled/GH.Runtime/PromotionVideoHandler.cs`: additional path to inspect.

Proposed packing: start with full side-by-side, 3840x1080 for 1920x1080 per eye,
and one decoder for synchronized eye pictures. Confirm actual decode performance
and eye routing on supported XR modes; do not assume native stereo metadata or
an existing flat composite will choose the correct eye automatically. Preserve
the UI composition and ordinary 2D presentation. Use a matching 2D fallback when
stereo media is missing; synchronize the actual selected asset/version for peers.
Shipping and installing remain mod-local; release/developer installers already
include the intro Content asset.

## Proposed conversion experiment

1. Prove eye selection with a known stereo calibration clip before inference.
2. Convert three five-second samples: quiet ambience, moving character, and a
   difficult accepted intro shot with the VR mask/hands. Compare output in-headset.
3. Estimate temporal depth per original shot; reset at cuts and preserve loops.
4. Reproject to the second eye and fill genuinely disoccluded regions. Keep known
   picture regions anchored to the original; avoid generative text/face changes.
5. Establish comfortable depth per shot and stable text/logo planes. Test mask,
   finger, hair, particle and transparent boundaries, cuts and loop closure.
6. Preserve source frame rate/count, duration and packet-copy audio. Inspect actual
   mono and stereo output, skip/completion/errors, scene lifetimes, grabs and peers.
7. Only after the pilot passes, batch the remaining films into an additional
   versioned media pack. Measure pilot GPU time/storage before quoting a total.

Candidate primary sources checked during planning:

- Video Depth Anything: `https://github.com/DepthAnything/Video-Depth-Anything`
  (temporally consistent video depth; offline route preferred for quality).
- M2SVid, 3DV2026: `https://github.com/google-research/m2svid`
  (released March2026; second-view inpainting/refinement, spatial/temporal tiling).
- StereoCrafter: `https://stereocrafter.github.io/`
  (comparison candidate for depth warping and temporal stereo inpainting).
- Unity2021.3 shader eye routing reference:
  `https://docs.unity3d.com/2021.3/Documentation/Manual/SinglePassInstancing.html`.
  The mod also has an existing MultiPass path; follow its real configuration.

No claim was made that these models are available as ready-made fal endpoints,
that the local GPU can run them, or that a full conversion has a known price.
No new stereo-specific budget was agreed. The old trailer's conservative budget
ledger is unrelated to a new stereo batch; do not infer leftover funds as approval.

## Validation to inherit versus rerun

Build678/679 intro media and focused playback proofs are documented in STATE
and the guide. Build679 native-video:93 assertions +10 controls, real packaging
and Debug/Release installer checks; zero-warning Debug/Release builds. No
headset first-save/skip or stereo result is claimed. Current dev has later
unrelated repairs; read the newest evidence rather than treating679 as current.

Relevant focused suites: native-video, native-playback, video-playback and the
flat-screen/compositor checks affected by actual implementation. Runtime changes
must follow the repository's worker, integration and scoped/full-gate rules.
Store screenshots/logs/results under gitignored `.planning/debug/`, reap own
processes and use `cleanup_completed_run`; preserve media authorities and captures.

# Quest B626 — bounded B625 rendering and procedural-loader correction

Status: source update for the Windows builder; no B626 Player or APK is built on
the agent host. Work remains on `feature/quest3-standalone`, separate from `dev`.

## Verified B625 evidence

The current `quest-capture-20261006T142146Z-7d6d0267.zip` identifies B625 through
its installed package, current startup banner, APK hash and completed local
installation receipt. Input starts `53b564e69a80`; APK SHA256 starts
`3d32aeb6232f`. The maintainer confirms immediate, correctly oriented intros,
then a thin rotating red line and a black menu with hands, rays, music and native
button hover sounds. The supplied `dev.gloomhavenvr.quest-20261006-161937.jpg`
shows that line against black; it does not identify which source pixels are wrong.

The actual delivered original spinner has intact sprite rects, pivots, vertices,
UVs and its 4096-square atlas. Its native sprite shader contains Vulkan programs.
There is no evidence for rewriting the original atlas, geometry or shader.
Unadapted generic XR blits remain in sprite crops and ordinary offscreen copies.
An independent real render fixture demonstrates that borrowing XR camera
matrices can clip an ordinary fullscreen copy even with a two-dimensional sampler.

MainMenu loads and its captured producers and visible layer27 consumers exist.
Previous tiny generic-blit measurements are not independent source-pixel proof;
they also measured the left background while the consumer used the right target.
Neither raycast sounds nor scene presence proves visible native canvas content.
Original menu initialization does not establish an Apparance readiness/alpha gate.
The exact cause of the black headset menu therefore remains open.

The current procedural launch reaches Wine and fails on GNU symbol resolution,
including `__errno_location` and `__assert_fail`. Current error/worker context must
not be replaced with the older B624 libc-entry failure or historical crash logs.
Payload presence is not successful engine execution: `fullGameReady` is false.

## Source changes

`QuestTextureCopy` and its authored resource shader use an ordinary sampler2D,
explicit fullscreen positions and preserved RGBA/crop/color-space state. This
path serves spinner captures, retained far-plane movie snapshots, shared eye
shifts and small pixel probes. Desktop continues to use its original blit path.
The public helper is compiled separately against the actual mod DLL to exercise
the generated `QuestGame.Campaign` assembly boundary.

Old Quest spinner caches without the new copy-recipe receipt are recaptured once
from native artwork. Desktop cache acceptance, player saves and configuration
remain intact. Two bounded spinner records include actual art, shader and scale.

Two existing menu handover samples retain native canvas/camera/pose facts, up to
six canvases and twelve graphics with inherited alpha, cull state and projected
position. Tiny asynchronous ordinary copies sample UI, left background and the
actually bound right target. At the second sample, at most three 32x16 RGBA8
records are logged. The collector validates the current build/input banner and
decodes them into fixed PNG filenames without extra ADB queries. These are
downsampled producer images, not eye captures or text-readability qualification.
Unsupported GPU readback remains an explicit availability gap without a GPU wait.

Pinned Android-only Box64 adapters address the seven audited required missing
GNU names. Errno is native thread-local Bionic errno; assertion arguments retain
their order and abort behavior. C.UTF-8 classification/case tables preserve the
GNU 384-entry layout and pointer-to-pointer contract. Reentrant random preserves
the original caller-owned GNU state, sequence and metadata. Catalog-free gettext
preserves diagnostic strings within the explicitly fixed worker locale. Linux
wrapper preprocessing remains unchanged.

Before native payload staging, an import gate reads actual required Wine ELF and
version tables and resolves compiled GO/GOM/GO2 bindings against the real Box64
exports and selected NDK API29 exports. Missing strong bindings fail staging.
Optional Wine display/DNS services are outside this headless worker gate; no
invented resolver-state implementation is supplied. The actual final whole
Box64 build and its positive staged-payload audit run on the user's Windows PC.
Small host/NDK fixtures do not substitute for that final artifact audit.

Each worker attempt starts an attributed current log with UTC, parent/child PID
and native input key. One previous tail is bounded to 256 KiB and collected via
an explicit optional path. No prefix directory, engine DLL or save is exported.

## Focused validation and remaining acceptance

- Real GL/Vulkan menu-copy fixture: 2,566 runtime assertions; ten GL defect
  controls. Asymmetric crops, alpha, cache receipts and render-state recovery
  are covered with stereo keywords active.
- Real Vulkan movie fixture: 45 runtime assertions and four defect controls.
  The focused GL movie fixture passes 78 assertions and three controls.
- The actual mod-to-Quest adapter assembly boundary compiles with zero warnings
  and errors. Original Player/import/full shader-matrix builds are not repeated.
- Collector: 67 cases pass, one platform skip; PNG CRC/rows/bytes, fixed roles,
  current-build attribution, invalid records and original log privacy are covered.
- Native adapter/import/launch checks: all 25 cases pass, including 245,760 GNU-oracle random outputs,
  all 384 entries for each ctype table, pointer/errno thread ownership, assertion
  abort, actual ARM64 NDK helper exports, negative ELF/binding/header controls
  and executable bridge log rotation. Android engine execution is still open.

Use the updated game-free builder with the existing owned PC installation and
cache location. Matching completed stages remain reusable; changed mod/native
inputs rebuild the affected outputs. Confirm B626 in the resulting installation.
Observe intros, spinner, menu and preview; wait at least twelve seconds after
MainMenu arrival before exporting with the updated capture script. The capture
automatically contains any available `quest-menu-glass.png`,
`quest-menu-background-left.png` and `quest-menu-background-right.png`.

A visible menu, successful original engine initialization, scenario generation,
Guildmaster, saves, multiplayer and mobile performance still need actual headset
acceptance. This update does not announce any of them as solved.

## Backend direction after this checkpoint

The maintainer explicitly authorized completing this checkpoint and then directly
implementing the planned Proton route. The next engine implementation uses
Android Bionic ARM64EC Wine/Proton plus FEX: its native Unix
components avoid translating an entire Linux-x64 Wine dependency tree. Retain
this bounded, tested correction as a comparison/fallback, rather than opening
another speculative optimization round on it.

The existing [ARM64EC/FEX evaluation](QUEST3-PROTON-FEX-ENGINE-EVALUATION.md)
records actual Android release inspection, launch/layout adaptations and open
sandbox/protocol/geometry/performance gates. Keep the original x64 worker, owner
DLL and existing IPC boundary. Qualify a small installed native probe before a
full Player integration; upstream Linux Proton alone is not a Quest runtime.
This architectural choice does not establish a cause for the black menu.

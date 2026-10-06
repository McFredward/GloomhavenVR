# Quest B625 — PC-installed content and original Campaign/Guildmaster runtime

Status: signed build and independent delivery readback in progress, 2026-10-06.
Hardware behavior is unverified until a capture identifies this exact package.

## What the B624 capture actually establishes

`quest-capture-20261006T083845Z-4e8b81af.zip` identifies B624 consistently through
its fresh banner and installed package. The supplied report describes an inverted
first intro and a persistently black menu. No matching new screenshot accompanied
this archive, so the precise rendered pixels have not been inferred from older images.

The legacy installer had supplied only470 files. The headset installed5,897 more
files during this first full-content run, from08:28:51 to08:34:36 UTC. This is a
real device-side archive verification/extraction cost, not proven repeated failure
of a completed warm cache. B625 moves that work into the PC installation.

The procedural worker terminates at its missing glibc guest `__libc_start_main`
entry before entering Wine/Engine.Start. The capture does not prove working
engine geometry generation or mobile performance. An older B623 Vulkan crash
embedded in historical logs is not a new B624 crash. Original unified-data and
native bundle startup also take substantial time after extraction; eliminating
extraction does not yet establish the requested few-second complete startup.

## Changes and their limits

The signed APK contains final game and mod installation inventories. The Windows
installer expands both banks on the PC, transfers changed files in resumable
batches and publishes each native completion receipt only after a complete
transfer. It neither deletes saves nor treats an interrupted transfer as complete.
Full-game startup reads the receipt instead of scanning/hashing all6,367 game
files or adopting/extracting a bank on the headset. An incomplete installation
stops with localized instructions to rerun the PC installer. Explicit
`--repair-content` verifies and repairs file bytes when needed.

The Android Box64 executable now retains the pinned upstream glibc guest-start
implementation alongside its Bionic entry. Actual ELF exports pass the focused
entry audit and the original DLL is unchanged. This removes a source-proven
missing-entry defect; it does not prove successful Quest engine initialization.

Guildmaster is included again by the maintainer's explicit correction. Original
mode admission, data loading and save validation/loading are retained. Workshop
and provider profile/cloud services remain excluded; required EOS session
transport remains allowed. Campaign, purchased DLC and Guildmaster all need
actual headset scenario/save/multiplayer tests.

Vulkan camera-video UV orientation follows the actual GPU projection sign.
Manual mip generation no longer competes with automatic ownership. A dedicated
Quest glass shader consumes the already-composed transparent menu RGB once.
Targeted GPU probes demonstrate the old second alpha multiplication can erase
valid RGB, but do not establish that this alone caused the B624 black menu.
Two bounded menu handover snapshots and small asynchronous GPU samples now
record producer texture, camera and presentation state without a per-frame
permanent diagnostic stream.

Full-game IL2CPP compilation uses Release native optimization while retaining
Development player diagnostics. Existing Steam Frame defaults and the initial
Vulkan eye allocation remain in force. Original imports and warm shader cache
are reused; no exhaustive shader compiler matrix is rerun for these changes.

## Focused verification completed before the build

Release mod compilation passes with zero warnings/errors. Focused checks pass:
390 builder tests;185 installer tests (23 platform skips);832 weaver assertions;
3081 platform assertions/17 defect controls;293 startup assertions/19 controls;
660 content delivery assertions/18 controls;103 actual filesystem Editor
exclusion assertions; actual SDK scope30 assertions. Restored full-scope and
separate diagnostic-scope tests retain their respective native behavior.
The actual Quest glass/video GPU gates exercise Vulkan and GL, and the focused
Android Vulkan delivery gate retains its28 observed banks/56 stages. These
are local source/serialization/compilation proofs, not headset picture proof.

## Next hardware run

Use the complete new Windows package and its current install script; APK-only
sideloading cannot complete the new expanded-content contract. Existing saves
should survive an update. Retain the install receipt/log as well as the capture.

1. Confirm B625 in the fresh capture. Record time from launch to first intro and
   from last intro to the menu. Repeat one cold application launch after quitting;
   neither run should contain a multi-minute content hash/extraction phase.
2. Observe all intros, their orientation/audio and a visible, usable menu in VR
   and passthrough. If the menu is black, leave it running at least15seconds so
   both bounded handover samples are captured. Capture a current screenshot.
3. Load an original Campaign scenario (and a purchased DLC scenario), inspect
   generated walls/terrain/obstacles/materials and interaction, then save, quit
   and reload. Report generation time and visible frame interruptions.
4. Enter Guildmaster, generate a scenario, save/reload and return to the menu.
   This mode is now enabled, not a disabled tooltip entry. Its dynamic generation
   is particularly useful for assessing the actual engine backend.
5. Exercise unchanged input, keyboard, audio and passthrough. If possible test
   an original session-code multiplayer connection with a PC player, recording
   both captures and the same scenario/seed.

Collect with `scripts/collect-quest-logs.cmd`; include the installer log if
installation failed. This run distinguishes backend initialization, source
rendering, presentation and overall startup cost. Success must be reported only
for features the actual headset run confirms.

# Steam Link availability repair coordination, 2026-10-17

## Follow-up in progress: cache preparation and Proton, next Build689

The maintainer tested688/fc5c954e7. Its exact SDK resource name,826688-byte
length and SHA256 were verified from the installed DLL in PowerShell; no Local
cache directory exists. The repair now prefers the inherited LOCALAPPDATA path,
normalizes separators before its local-drive guard and reports exact preparation
stages. Focused tests exercise actual resource extraction, reuse and corrupt-cache
refusal, beyond the old resource-only check. A separate small worker removes the
Windows-only vrserver.exe/vrcompositor.exe census: Linux hosts run this Windows
game/helper under Proton, whose native SteamVR is reached through its existing
C:\\vrclient bridge. The SDK Background initialization owns the no-server guard.

Please reserve **689 for this bounded follow-up** and move the extended stereo
handover to690 or later. This lane still owns no movie/runtime/UI/installer paths
and will not touch unfinished media. NetProtocol/STATE changes will be limited to
the new note/number at integration. No Frame action or new receiver install.

Publication boundary: shared dev currently contains four independent, unpushed
stereo-library checkpoints through cd1092c30. Their22-file installer preflight
cannot yet succeed without the remaining conversions. To avoid publishing an
unfinished batch, the root will validate/push only its bounded689 repair on top
of the latest published origin/dev fc5c954e7, then merge that published head into
the shared local dev. The stereo checkpoints remain intact and unpublished until
their owning thread completes them. Do not publish the partial media catalogue
merely to deliver this identity fix. No worker branch is pushed.

## Message to the independent stereo-video thread

The primary passthrough thread is actively repairing the maintainer's newest
Build687 hardware failure: Frame+Steam Link native toggle is hidden because the
PC identity query stops at `RuntimePath` before launching its isolated reader.
The supplied registry is valid and the user confirms a runtime-side SDK DLL is
present. The app should not depend on or separately parse that optional SDK copy.

This bounded repair was already in implementation when the independent thread
announced its new batch. Please allow **Build688** for this small repair first;
base the extended stereo-video batch on the resulting dev and use **Build689**
or later. No movie, video output, MR shader or FlatScreen edits are part of it.
Do not overwrite its build note or newest STATE entry.

Owned by the passthrough root: `Core/SteamLink/SteamLinkConnection.cs`, focused
`tests/steamlink-connection/*`, `Net/NetProtocol.cs` build688 note, and the newest
STATE entry. A worker from dev9f48730df owns only the official pinned public
OpenVR SDK `openvr_api.dll`, its provenance and BSD3 notice, and ONE added embedded
resource in `src/GloomhavenVR/GloomhavenVR.csproj`. The worker's csproj edit is
bounded to that resource; please retain it in subsequent media project edits.

This session has no outbound cross-thread messaging tool, so this shared file is
the handoff. No Frame write/restart, new archive or extra video conversion is
requested by this repair. Its handover will use focused checks and a strict
Release build, reusing unchanged raster/wire evidence.

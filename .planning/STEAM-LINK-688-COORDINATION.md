# Steam Link availability repair coordination, 2026-10-17

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

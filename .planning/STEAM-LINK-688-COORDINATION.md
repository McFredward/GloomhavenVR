# Steam Link availability repair coordination, 2026-10-17

## Urgent produced-artifact repair in progress:691, stereo handover692 or later

Newest hardware is689/f71 and stops at cache-folder again. The actual produced
DLL now proves the cause: `path.Skip(2)` binds to GLOBAL GH.Runtime.StringExtensions.Skip,
whose Substring(count-1) leaves the drive colon. All normal local Windows paths
are rejected, independently of valid environment/known-folder values. The
earlier source-only fixtures omitted GH.Runtime and therefore bound Enumerable.Skip;
their green result did not cover production binding. Actual UnityMono+689+real
GH.Runtime reproduces this with five controls; IL confirms the wrong call.

The identity root will ship a single explicit Enumerable.Skip call, with hostile
global-extension fixture coverage and actual produced-DLL verification. Since
the independent thread has already reserved690 locally, this fix takes691;
the complete stereo handover must advance to692 or later. No690 binary is handed
over. Publish ONLY the bounded repair on published dev689, then merge into shared
dev retaining all media checkpoints. Avoid pushing the unfinished22-file roster.
Owned paths remain SteamLinkConnection.cs, its focused tests, scoped notes and
the identity build note. No folder fallback, native helper or Frame patch change.

## Follow-up published: cache preparation and Proton, Build689

Published origin/dev is f71a97db4 (689), focused2/2 and source4/4, strict Release
0W/0E, actual embedded payloads/build confirmed. Shared dev incorporates it through
merge4e4929f49, retaining all stereo checkpoints. Its exact new helper pin is
cec82e68968441902e717c6bc595ff0ae0b0b03ffe72bc5eb26cbfe19c40a30f.
The stereo thread confirmed690 or later for its complete batch. No need to rerun
the bounded identity suites when only its independent movie files change.

The maintainer tested688/fc5c954e7. Its exact SDK resource name,826688-byte
length and SHA256 were verified from the installed DLL in PowerShell; no Local
cache directory exists. The repair now prefers the inherited LOCALAPPDATA path,
normalizes separators before its local-drive guard and reports exact preparation
stages. Focused tests exercise actual resource extraction, reuse and corrupt-cache
refusal, beyond the old resource-only check. A separate small worker removes the
Windows-only vrserver.exe/vrcompositor.exe census: Linux hosts run this Windows
game/helper under Proton, whose native SteamVR is reached through its existing
C:\\vrclient bridge. The SDK Background initialization owns the no-server guard.

**689 is this published bounded follow-up**; move the extended stereo
handover to690 or later. This lane still owns no movie/runtime/UI/installer paths
and will not touch unfinished media. NetProtocol/STATE changes will be limited to
the new note/number at integration. No Frame action or new receiver install.

Publication boundary: shared dev currently contains four independent, unpushed
stereo-library checkpoints through cd1092c30. Their22-file installer preflight
cannot yet succeed without the remaining conversions. To avoid publishing an
unfinished batch, the root validated/pushed only its bounded689 repair on top
of the then-published origin/dev fc5c954e7, then merged that published head into
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

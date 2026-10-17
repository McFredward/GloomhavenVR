# Steam Link identity preparation follow-up — Build689

## Evidence and decision

Build688/fc5c954e7 logs on the maintainer's Windows4090 still stop at SDK
preparation. The installed assembly exposes the correct public SDK resource,
826688 bytes with SHA256 `5e591aa3f4a991edcc8910921833edafa67a1b117849bbb4e235df9833bed7a3`.
PowerShell finds no `%LOCALAPPDATA%\GloomhavenVR` directory. These facts exclude
missing/wrong SDK bytes; they do not reveal the old known-folder API return,
HRESULT or exact pre-directory guard. Player.log being in LocalLow is normal
and does not establish the independent Local cache path.

Prefer Windows' inherited LOCALAPPDATA before Mono's known-folder wrapper,
normalize separators before the local-drive guard, then retain exact resource,
PE/hash and no-reparse checks. Distinguish finite folder, resource, create,
identity and cached-pin failure stages. Do not log arbitrary helper output or
paths every frame. No manual cache creation or SDK download is required.

Earlier fixtures verified the resource but did not execute the actual extraction
path. The focused fixture now writes both real embedded resources to a fresh
private directory, reuses them without rewriting, and refuses/preserves corrupted
cached bytes. Its only substituted boundary is the OS Windows ancestor validator;
portable Linux I/O is not evidence of a successful Windows shell-folder call.

## Proton boundary

Gloomhaven is a Windows game; Linux hosts execute this same PE helper under
Proton. The previous Windows vrserver.exe/vrcompositor.exe census was invalid for
native Linux SteamVR. Use the public SDK Background=3 no-server contract instead,
map121 to not_running, and retain the caller's active SteamVR OpenXR-session gate.
Successful child initialization gets exactly one own shutdown; failed Init gets
none. Proton runtime/prefix/OpenVR variables remain inherited. Valve's SDK uses
Proton's existing C:\vrclient mapping; no custom host registry parser, nativeLinux
helper, native provider redistribution or second Unity VR client is introduced.
A working Linux SteamVR/Steam Link installation is still an external prerequisite.

The helper stays42496 bytes; new SHA256 is
`cec82e68968441902e717c6bc595ff0ae0b0b03ffe72bc5eb26cbfe19c40a30f`.
Source/compiler/ABI provenance is adjacent to it. Exact Steam Frame/vrlink
classification, unsupported-transport fallback and receiver-unverified status
remain unchanged. No shader, source packing, MR default or wire change.

## Checks and delivery

- Integrated focused suites2/2:118 managed checks;98 metadata+105 production
  Background-query checks;37 availability assertions with4 causal controls.
- Native203 also pass ASan/UBSan/leak checks, byte-identical MinGW rebuild and
  removal of Toolhelp process-census imports.
- Affected source4/4; strict Release0warnings/0errors. Actual shipped-DLL reflection
  confirms both exact embedded resources and689.
- Private inspector first expected826689 instead of826688; correct only this
  inspector and repeat it. A separate initial scheduler launch refused its
  nonempty output directory before any tests. Both setup failures are retained.
- Completed fixture caches are removed after processes are reaped. Preserve
  source/results/logs/hardware facts under `.planning/debug/steamlink-689-followup/`.
- This is a bounded gate, not a new full wire/raster/game suite. Actual Windows
  and Proton headset identity remains unconfirmed until a live stream is tested.

Publish only this bounded repair over the latest published dev688. The shared
checkout's independent unfinished20-film catalogue remains local until its owner
finishes; merge this published689 back into that shared dev without discarding
its checkpoints. The normal689 installer still uses the complete two-movie
catalogue of688. No Frame file, restart or receiver reinstall is needed.

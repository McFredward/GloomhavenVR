# Quest B627 — Android Proton/FEX builder handoff

The maintainer authorized the backend switch immediately after the B626 source
checkpoint on 2026-10-06. Implementation remains on `feature/quest3-standalone`;
the parallel `dev` tree is untouched. APKs are built on the maintainer's Windows
PC. This handoff contains the public builder source, not an agent-host Player.

## Runtime boundary

The default full-game recipe is `proton-arm64ec-fex`. Native Android/Bionic ARM64
Proton Wine provides the Windows ABI; official FEX translates the unchanged
Windows x64 worker and the owner's original `ApparanceEngine.dll`. Unity,
rendering, the current VR mod, managed gameplay and networking stay native.
The original twelve exports, GHPR framing, callbacks and task-buffer leases
remain unchanged. Campaign, Guildmaster and owned DLC stay in scope.
Workshop and store/cloud services remain excluded.

This uses the Android adaptation of Proton Wine, not Valve's Linux launch script
inside Android. It contains no emulated x64 Unix/glibc Wine tree, proprietary
launcher shim, Steam client or new account service. The previous
`box64-wine9` recipe is an explicit developer comparison only; qualification
failure stops the build instead of choosing a different backend.

Public artifact pins and source/license provenance live in
`tools/quest-procedural-runtime/proton.lock.json`. The Wine WCP is 98,079,159 bytes,
SHA-256 `fffa467241bdae3eacd6ceb7e8096bb7793d617ce53a198dae8bc63a3453f595`.
The official FEX acquisition package is 1,693,160 bytes, SHA-256
`c62210a501153885dede187f433e9a107d8d7a91b6959ba17e8a35a05c62e7fa`.
Only its ARM64EC DLL and notices are selected. Published artifact identities,
release source references and PPA build recipes are recorded separately;
an exact Git-to-binary attestation is not claimed.

## Android packaging and cache ownership

Every executable Unix program/module resides in APK `nativeLibraryDir`.
Source-owned loader adaptation maps only Wine's fixed logical installed paths;
ordinary libc calls retain their implementations. ELF filename relocation changes
bounded dynamic strings without changing program offsets/instruction sections.
The complete consistent 64-bit Wine PE/data tree remains interpreted owned-build
content. Optional desktop/Steam Unix drivers and 32-bit support are excluded.

The independent actual-byte audit checks Android ARM64 ELF bindings and both
native ARM64 bootstrap and ARM64EC/x64 PE import views. In-memory ARM64X fixups
select the real caller-specific exports without rewriting delivered PE files.
Native Wineboot/services/winedevice are included in the dependency proof.
Unknown required imports, wrong architectures and mixed backend files fail
before delivery. The audit runs at preparation time, not at headset startup.

The new private `proton-prefix` uses a real internal `system32` directory with
individual links to installed builtins. New Wine initialization outputs stay
private. The pinned [Wine builtin installer](https://github.com/GameNative/proton-wine/blob/555aa70febb7d36e82d96697b554ff8f4fe0bb1a/dlls/setupapi/fakedll.c)
recognizes those links without copying over their targets. Wine performs its
original initialization; no fabricated ready-prefix receipt is used.
FEX selection is registered before startup. Headless/null graphics and server
synchronization are selected; desktop drivers, ntsync, fsync and esync are disabled.

Backend choice participates in the immutable input key. Native runtime caches
depend on their own source/pins/NDK, independently of ordinary mod edits.
Existing original-game conversion caches remain available where their exact
recipe and inputs still match; a different backend cannot reuse an old Player.
The small native inventory is hashed during staging and actual Unity import;
the signed APK inventory and Player report must agree on backend and filenames.
`stagedProceduralNativeFiles` records pre-Gradle hashes, not a claim that stripped
APK ELF bytes are identical. Existing APK architecture/CRC/signature gates remain.

## Diagnostics and next hardware evidence

Retain the normal Wizard support ZIP on build failure. It includes bounded native
compiler logs and the compact Proton stage summary, including actual missing
bindings, input identity and audit hash. Full local audit evidence remains in
the native cache. The collector adds `quest-proton-engine.log` from the fixed
internal prefix path; current/previous worker logs retain backend/input banners.
It never enumerates or exports game binaries, prefix registries or saves.

B626's ordinary spinner/video copies and native menu/right-target probes remain
included. The collector can export up to three tiny PNGs from the matching
current startup log without extra device queries.

For the next locally built B627 hardware test, observe intro, spinner and menu;
leave the menu active for at least 12 seconds before capture. If usable, exercise
the existing 3D preview, then load a Campaign or Guildmaster scenario and record
load time, generated scene, responsiveness and save/reload. Retain a capture even
on immediate engine failure. Do not treat a working prefix or menu as proof of
procedural synthesis equivalence, multiplayer or mobile performance.

Source/import/native-compile checks do not establish Android Wine/FEX execution,
JIT permission, first-prefix latency, sleep/resume, scenario generation or a
correct headset picture. The old engine failure was not a source-proven cause
of the black menu; that rendering question remains separate.

## Focused validation

The final integrated tree passes 76 native-runtime cases with the real NDK and
pinned Wine/FEX artifacts, 42 builder cases, five native inventory cases, 14
release/support cases and 67 collector cases (one optional PowerShell skip).
Actual Unity import of all 28 staged native programs passes 174 assertions,
including Android/ARM64 inclusion, disabled preload and failure controls.
The actual separate Quest/mod assembly boundary and strict Release compilation
have zero warnings and errors.

The actual default `campaign_native.stage` call completes with the original
DLL byte-identical before and after staging. Independent qualification records
27 procedural ARM64 ELF files / 1,156 native import occurrences and 126
native/EC PE contexts / 19,904 PE import occurrences. The signed small inventory
declares those programs plus the original voice ABI: 28 actual ARM64 files.
Wrong backends and a changed actual staged program are rejected. The integrated
source-stage control takes 8.06 seconds on this build host with verified public
runtime cache; this is neither whole-Windows-build timing nor headset startup.

Compact/full audit, native compiler and integration receipts remain under
`/home/claw/quest3-local/build/evidence/B627-proton/`. Real Unity importer evidence
is `.planning/debug/quest-native-plugins/run-_21nwhud/`; the separate assembly
boundary log is `.planning/debug/quest-proton-boundary/build.log`.
No Player/APK, unrelated NPC/wire gate or exhaustive shader compilation runs
for this builder/backend update. Source release identity is published in the
adjacent Builder ZIP audit rather than embedded here before the final commit.

# Quest original-game startup checkpoint — ModBuild 612

This isolated checkpoint targets the real Bootstrap -> Intro ->
Gloomhaven_unified -> MainMenu flow. It is a privately built startup diagnostic,
not a playable campaign or the complete VR mod. Quest work remains on
`feature/quest3-standalone`; concurrent `dev` work is not integrated or pushed.

## Evidence that motivates this checkpoint

The maintainer accepted B611 animation, lighting, textures and button handling.
The supplied private capture identifies the installed B611 APK by SHA-256
`1751bc9c2301ad9c292eff5b53f0ffaae7d0a91cc2378a2225d3a5bfb9075857`.
Its 14 VrApi samples report 72–73 FPS at a 72 Hz target, no stale frames and
0.44–0.58 ms App time. That short, small synthetic scene establishes neither
full-game memory requirements nor sustainable campaign performance.

Right-stick vertical input now raises/lowers the inspection origin without
changing tracking poses. Text/window aliasing is retained as a later presentation
quality issue; this checkpoint does not spend a hardware round polishing it.

Windows ADB put successful pull receipts on stderr. The prior collector treated
those successful copies as failures and discarded them. The repaired collector
accepts verified success on either stream, retains exact byte-count checks and
continues to reject incomplete/failed transfers. Startup log/state are optional
for backward compatibility with the existing probe.

## Test goals

| Gate | Observe or exercise | Required evidence |
| --- | --- | --- |
| Provenance | Startup panel identifies B612 and input key; dummy profile on this host | Panel photograph and installed APK hash |
| Owned content | First launch extracts and verifies real Rulebase and catalog files; subsequent launch reuses verified content | Startup state/log reach contentReady and addressablesReady |
| Original startup | Wait for original intro/menu; report a stuck state instead of repeatedly relaunching | Scene chronology Bootstrap, Intro, Gloomhaven_unified and MainMenu, original errors |
| Original UI | Aim with controller and select original menu entries, open/close original settings | Native callbacks execute, no exceptions or unresponsive controls |
| Excluded modes | Guildmaster and Steam Workshop remain visible, disabled, with hover explanation | Photographs and no excluded action |
| Native MR | A toggles real passthrough in the shared OpenXR session | Transparent headset output only when passthrough is active; VR fallback remains opaque |
| Navigation | Left stick moves; right stick turns and changes height; return from system menu with sticks held | Stable tracking, neutral resume, no unintended jump |
| Android network | From the original multiplayer menu, enter a current compatible PC session code | Original client connects and joins, or exact connection/error chronology |
| Lifecycle | Open/close system menu, sleep/wake; exit and launch again | Focus/pause state and original startup behavior |
| Load/performance | Let the original menu settle, interact, then capture without closing the app | VrApi samples, Android memory and startup errors |

Run the Windows installer from the complete private test archive. Updates use
`install -r`, the same package and signing key; local app data is retained.
After testing, keep the Quest awake and run `Collect-Quest-Logs.cmd`. Place the
resulting capture ZIP and photographs under the main checkout's ignored
`.planning/debug/quest3_probleme/`. Do not upload owned game assets publicly.

For the first session-code experiment, use a compatible unmodified PC host with
crossplay enabled and a base-game campaign. Enter its private code on the original
Quest multiplayer screen. The diagnostic connects through the original network
path; it must report an actual content/admission failure if campaign loading is
reached. A connection followed by an unsupported campaign load is narrower
evidence than a playable session. Do not repeatedly retry campaign loading to
work around the explicitly missing generation/scenario slice.
Collect immediately after the invite attempt. Original client errors reach
Unity/logcat, but ordinary connection stages and some native UI error codes are
only in the collector's recent 3,000-line logcat window, rather than durable
startup state. The bounded startup log retains up to 24 original Unity errors
with stacks; current and previous logs each stay within 256 KiB.

The original invite field suppresses Android's soft keyboard. The narrow pointer
bridge opens the game's existing keyboard and retains its native key callbacks,
caret handling and validation. Its controller-only auto-hide component is
temporarily disabled while the pointer keyboard is visible. Keyboard input is a
hardware gate rather than a claim established by the source fixture.

## Boundaries and remaining implementation

The original scene/plugin identities and original callbacks are recovered from
the owned installation. Imported UGUI/InputSystem script identities are remapped
against actual package MonoScripts, with callback bytes preserved and failures
rejected before writes. Startup Addressables are genuinely rebuilt for Android;
exact original keys/labels route to real native catalog locations. This is not an
empty-success loader. File-backed content uses a verified private extracted root;
original save paths and save serialization are not replaced.

The recovered export contains 33 path groups differing only by case, which Unity
cannot reliably import together. The generated project relocates 13 original
units (32 files) to unique paths without changing their bytes or GUIDs. Original
Resources keys, Addressables keys/labels and callback bytes are preserved. The
audited private migration verified 17,890 original files and an idempotent repeat.
The selected original player also requires 34 built-in Unity module packages;
these are derived from its managed modules and the exact editor catalog. An
actual Android asset build with that set retains ParticleSystem and other native
components, instead of silently dropping them during serialization. The private
catalog preflight built 405 eligible assets, 762 original-key aliases and 10
labels. It does not establish that the headset loads or presents those assets.

The first complete player build exposed an invalid exported ComputeShader:
`EyeHistogram.asset` contains Windows variants, an empty source and an unset
compilation platform. Unity crashed while serializing it. The generated project
now restores Unity's legacy-v1 `EyeHistogram.compute` and its two includes from
commit `933df236f509ed64ae5763ed57af33f2342cd1c2`, with pinned hashes and MIT license.
The original GUID, Resources key and used kernel ABI are preserved; original
dump/meta bytes move outside Assets. Unknown compute shaders or changed inputs
fail before writes. Real Unity Android Vulkan/GLES3 assetbundle builds pass both
with graphics and headlessly; the latter compiles four variants without cache
hits. This proves source portability, not eye-adaptation pixel parity. Private
headless evidence SHA256:
`92e93f389f79ef1ad9bf57317b75456a96db3cccf52fe7226c3833fffe4b362c`.
The source and remaining shader boundary are documented in
[startup recovery](QUEST3-STARTUP-RECOVERY.md).

The first native player compilation also exceeded Clang's default expression
nesting limit in generated mod code and exhausted host memory while optimizing
two large recovered assemblies. This startup target uses IL2CPP's Debug compiler
configuration and `--compiler-flags=-fbracket-depth=1024`; managed expressions,
rules and wire bytes are unchanged. A real Unity 2021.3.5 ARM64 fixture confirms
`-O0` and the depth flag in all 204 IL2CPP native compile actions, including a
300-parenthesis negative control. Its private evidence SHA256 is
`21c7fee10ab780371d05325c1c08e72c0f6b7c3ee3ba62b2d63c48c50b6f4101`.
The APK receipt records these settings. This diagnostic can establish native
execution and menu behavior; release configuration and sustainable game
performance remain separate gates.

NDK r21's BFD then reported `R_AARCH64_CALL26` relocation overflows in the large
unoptimized player. LLVM LLD successfully linked the exact same 1,091 objects;
its executable sections total 204,489,144 bytes, including the separate `il2cpp`
section, and 57,607 actual AArch64 thunk symbols are present. A separate real
Unity APK build confirms forwarding of `--linker-flags=-fuse-ld=lld`. The builder
also sets Unity's supported `UNITY_IL2CPP_ANDROID_USE_LLD_LINKER` selector only
in the startup build subprocess. No editor binary or global environment is
changed by this recipe. Private executable-section evidence SHA256:
`7d53f106a5baf7ebeee70910fdcaaa5f5df492b22965ee168ddb4217d83b930d`.
Actual Unity forwarding evidence SHA256:
`3a66356ebf4f26a30ce77a3064c20e3e9228e036cb044f7132acf4bb81f3a75c`.

The private APK is built on Linux. The Windows installer is independent of the
full Windows recovery/build pipeline. AssetRipper's sequential unique-name
export avoids a source-proven overwrite on a case-insensitive filesystem, but
colliding Resources paths can receive different names there. An actual Windows
recovery must still prove original Resources association, GUID/content retention
and successful native import before claiming a reproducible full Windows builder.

The platform adapter uses the original generic platform and embedded local PC
identity. Local readiness does not assert an online Steam/EOS credential. Store
services, cloud, Workshop and Horizon are excluded. Necessary multiplayer EOS
remains separately authorized; this offline startup target does not initialize it. Voice-room opt-in is visibly disabled in this diagnostic because the original native Opus encoder has no verified Android delivery. Game room joining remains enabled.

The original Photon client connected to the live Master/default lobby on the
development machine using the original game configuration and null additional
authentication. It joined no game room. This proves that backend connection route
on that machine; Android transport, room joining and crossplay remain hardware
gates. Protected original rules/network types remain unchanged.

Apparance's original Unity lifecycle enters an unavailable Windows native engine.
Only those lifecycle callbacks and the loader cleanup refresh call are suppressed for this explicitly menu-only
target; genuine managed packet release and clearing are retained; generation APIs are unchanged and no generation success is fabricated.
Campaign geometry still needs an actual compatible runtime or verified prebaking.
The recovered startup slice also retains 230 shader placeholders and a later
AreaEffectSpriteAtlas gap. Six original TextMeshPro shader families use pinned
official package sources; original full shader appearance remains unverified.

The current mod is freshly built and statically woven. Original BepInEx config
and logging implementations are retained in a derived AOT closure, but the mod
plugin is not activated. Its real Unity lifecycle, runtime dependency discovery,
shared XR ownership and package API compatibility require a separate proof.
The temporary world-space menu presentation therefore does not certify full-mod
visual parity. Campaign load, one complete small scenario, original save/reload
and modded/unmodded peer gameplay must pass before declaring the game port ready.

## Build and validation receipt

The real Android ARM64 IL2CPP player build succeeded with Unity 2021.3.5f1.
All 1,091 actual native compile actions use `-O0` and
`-fbracket-depth=1024`; the actual IL2CPP link uses LLD. Private final compiler
evidence SHA256:
`ff910fd5980d4a40b0d43823eee1772b18a0ce5ff4821b1b9e2c23c442f5b7f3`.

| Item | Verified value |
| --- | --- |
| APK | `GloomhavenVR-Quest-B612.apk`, 1,109,713,887 bytes |
| APK SHA256 | `567722d511049f183e35236427d4128ba6ee056398df7c4543b982efc8bf3797` |
| Input key | `430fd5ea2730a2353be04c93d90c49dd7c4ede27ebc1ef6bd94122d9cf4f4fea` |
| Frozen runtime/tool source | `3e4f8edbdc2cb054ed7eb817bd5749b0d38b95be` |
| Package | `dev.gloomhavenvr.quest` |
| Signing certificate SHA256 | `1412542b0b4cac2f1bc4941cbb4b01a5556eb8da2c34709086ab9375fd33c012` — unchanged from B611 |
| Scope | `startup`, development diagnostic, full game unavailable |
| Identity | `Quest Local Test (DUMMY)`, Steam/account IDs `0` |

The embedded APK input manifest matches the immutable prepared input exactly.
Actual Android tools verify the package, signing, ARM64 native libraries and
IL2CPP metadata; no mandatory eye-tracking feature or eye permission remains.
The source-dirty marker reflects pending developer documentation at freezing.
The later packaged installer-only change gives this large APK 1,179 seconds for
wireless installation (bounded maximum 1,800), with no retries or uninstalls.

One complete 111-suite run passed 110 suites and failed the existing Town mirror
fixture's first playback assertion. Exact bound production hashes match prior
successful runs; the separate full production run passes 231,970 assertions.
The requested 0.13-second wait took 0.728 seconds in that successful run. The
failed run had no timing evidence, so its cause remains unproven. Fixture-only
instrumentation now records timing/session/pending state after the original
single tick, without changing assertions, delays, retries or production code.
The integrated focused suite, including all 21 negative controls, passes.

Final source/tool tree `ed9162ff` passes 14/14 source suites and the complete
111/111 local suites (679.6 seconds), including 130 installer controls, 57
recovery controls and 51 builder controls. All 286,760 wire/golden assertions,
three UnityFS bundle checks, 1,191 figure derivatives in 49 compatible parts and
the surface census pass. Strict mod Release has zero errors and warnings;
five English/German document pairs and developer links are checked.
Complete local report SHA256:
`8b41af93d5c039ec14f5711f6002c364ebab2a37e1fb567e00ddbb2e31092182`.

The guard command returns 1 because its preserved Build-601 baseline predates
authorized Frame602–608 work. No baseline was rewritten. A separate fresh
compiled review against frozen, reviewed Quest609 accepts 1,166 files:
1,156 identical, zero additions/removals, eight types with only 609 -> 612 build
constants, QuestText's 50 additional bilingual literal keys and branch metadata.
Existing QuestText keys, method structure, assembly references and all 26
resources are unchanged. Scoped review receipt SHA256:
`8b2e4d722995b6c1370594454d2f3231e2eb4010e1185a0fa25f9f5c3712b375`.
This is passed subordinate checks plus an accepted semantic review, rather than
a zero-exit guard command.

The private Windows archive contains the actual APK/handoff, installation and
capture entry points, their 15 exact source-matching dependencies and this
procedure. A default dry-run from an independent directory validates the actual
APK and selects its diagnostic/dummy handoff; collector help works without a
checkout. No ADB operation or headset test ran on the build host. Hardware
outcomes remain pending until a new capture and maintainer observations.

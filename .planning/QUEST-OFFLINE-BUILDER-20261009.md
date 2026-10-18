# Quest Builder offline feasibility, 2026-10-09

## Decision

The conversion and signing algorithms do not inherently require the Internet.
A complete, version-matched local tool and package set can support an offline
build. The current source Builder ZIP does not contain that set, and an earlier
successful setup does not yet prove an entire subsequent build is offline.

The practical first target is **prepare this workstation while online, then
build and resume while offline**. A separate dependency archive can make
provisioning reproducible and portable where the individual component terms
permit it. A completely fresh, disconnected workstation is a different target:
Unity activation is an independent prerequisite that a generic tools archive
cannot supply. Profile-only APK updates have a much smaller dependency set and
are the easiest path to guarantee offline first.

This audit describes source checkpoint `07ebc472d3b68d1f8bb4faa7f60afa575d548bbb`
/ ModBuild 647. It does not implement an offline bundle, change runtime game
behavior, or certify a Windows/Linux whole-APK build with networking disabled.
The user requested an early online check and evaluation of offline operation;
the main implementation lane separately owns the bounded network preflight.

Offline building and offline gameplay are separate promises. The selected
Quest game preserves local play without Store/Cloud/Horizon services; its
authorized existing cross-platform multiplayer may still need EOS/transport
network services at runtime. Those multiplayer requests are not reasons to
require Internet access while converting local game files into an APK.

## Current dependency inventory

Let `W` denote the selected Wizard workspace: `%USERPROFILE%\.ghvrq` on Windows,
`~/.ghvrq` on Linux by default. `B = W/build`. Let `R` denote the extracted
Builder source folder. These are separate locations: replacing the source ZIP
preserves `W`, but moving `R` can change Python runtime identity.

| Dependency and acquisition | Actual local location / reuse boundary | Offline consequence |
| --- | --- | --- |
| Launcher Python. Windows pins CPython 3.14.8 from `api.nuget.org/v3-flatcontainer/python/3.14.8/python.3.14.8.nupkg`. Linux can use a suitable existing Python 3.11+ with `venv`, `ensurepip`, and SSL; otherwise pins Astral's CPython 3.13.16 release from GitHub. | `R/scripts/.quest-python/python-3.14.8/` and the retained `.nupkg`; Linux `R/scripts/.quest-python-linux/python-3.13.16-20261003/` and retained `.tar.gz`. Ownership, location, version and file proofs qualify these. | This happens **before the browser Wizard exists**. A Wizard-only network check cannot diagnose its first download. A portable dependency source must also be understood by the CMD/PowerShell and Bash bootstraps. |
| Launcher venv. The current installer requirement file contains only comments. | `R/scripts/.quest-venv/`; owned location/base-interpreter/requirements markers. | No pip package download is currently necessary merely to show the Wizard. Do not invent a PyPI prerequisite for the stdlib UI. These venvs are not generally relocatable; rebuild from a local runtime/archive. |
| Build/conversion Python packages from `pypi.org/simple`, with file payloads from PyPI's CDN. Exact hash manifests include bootstrap `setuptools`/`wheel`, the `tpk_ar` source distribution, conversion wheels, CMake/Ninja and procedural `zstandard`. | `B/tool-cache/build-python-<pythonAbiHash[:16]>/.ghvr-build-python.json`, with the platform's venv Python and a matching `requirementsKey`. `python_abi()` also binds the base executable. | The current installer runs three phases when this receipt changes: bootstrap wheels; pure Python `tpk_ar` with `--no-build-isolation --no-deps`; remaining binary packages with `--no-build-isolation`. A warmed pip HTTP cache is insufficient as an offline contract: commands explicitly select the online index. Offline needs a complete ABI-specific wheel/source directory plus `--no-index --find-links`, retaining hashes. |
| General build tools: .NET 8.0.425, .NET 10.0.401, and Windows MinGit 2.51.0.windows.1. Microsoft/GitHub pinned archives are declared in `tools/quest-wizard/tools.lock.json`. | `W/tools/downloads/<name>-<version>[-linux-x64].{zip,tar.gz}`; extracted `W/tools/<name>-<version>[-linux-x64]/wizard-tool.json`. Linux Git is an existing system executable >= 2.25. | Valid completed archives can provision offline. Linux system Git and its distribution libraries are additional host prerequisites; the current Wizard does not install them. Full/mod builds need these tools; profile-only updates skip them. |
| Standalone .NET 10 recovery fallback for direct Builder CLI use. | `B/tool-cache/dotnet-10.0.401-<rid>/` and pinned archive, via `dependencies.dotnet10()`. | The Wizard already supplies its .NET 10 path, so this extra SDK copy is conditional. The offline dependency graph must not require both copies when an explicitly qualified SDK suffices. |
| Mod source and three compile-time XR reference assemblies. Public Builder sources are already shipped; remote checkout is an alternative. Missing XR sources are fetched from GitHub `needle-mirror/com.unity.xr.{management,core-utils,openxr}.git`, at the versions declared by the selected source. | Immutable owned checkout under `W/source/<session>[-<releaseHash[:16]>]`. Derived DLLs are `libs/RuntimeDeps/Unity.XR.{Management,CoreUtils,OpenXR}.dll`; cached package sources are `tools/RuntimeDepsBuild/sources/<package>/package.json`. | Existing target DLLs skip both Git and compilation. A source `package.json` alone skips Git but still invokes `dotnet build`, including implicit restore. New release workspaces omit these local derived DLLs by design; an offline preparation pack must provide qualified package sources/restore data or reproducible compiled tool references. A full public mod ZIP avoids cloning its own source from GitHub. |
| Unity Hub 3.22.2 and Editor 2021.3.5f1 (`40eb3a945986`), Android Build Support, SDK/NDK and OpenJDK. Hub's CDN/installer modules and sign-in services are involved in fresh setup. | An existing discovered/selected Editor, normally installed outside `W`, or Hub-managed install; `Data/PlaybackEngines/AndroidPlayer/{NDK,SDK,OpenJDK}`. Hub installer is in `W/tools/downloads/`; Linux AppImage in `W/tools/unity-hub-3.22.2/`. | Presence does not establish licensing. `unity_setup._probe()` executes a fresh small empty Editor method and expects its actual completion marker. The current successful probe is the authority for the current machine/run. A tools archive cannot carry somebody else's activation. |
| Unity Package Manager package graph. Template names include Input System 1.7.0, XR Management 4.5.0, OpenXR 1.13.0 and built-in modules. Full original recovery preserves the recovered manifest and adds Addressables 1.19.19, rather than using only the template's graph. | Generated project's `Packages/manifest.json`, resolved `packages-lock.json` and `Library/PackageCache`; Unity's global registry cache, normally `%LOCALAPPDATA%/Unity/cache` or `~/.config/unity3d/cache` for this Editor generation. Embedded UGUI is produced locally from the installed Editor. | A template-only cache probe misses recovered packages and all transitives. Offline needs the entire exact resolved graph, including metadata, sources, dependencies and any Git packages. Prefer local `file:`/embedded dependencies or a prepared registry cache; never delete/recreate the manifest in a way that silently drops native modules. |
| Instrumented AssetRipper source used by full recovery: pinned archive `https://github.com/AssetRipper/AssetRipper/archive/1ac666f.tar.gz`. It is built locally with .NET 10. | `B/tool-cache/full-recovery/identity-<sha256(QuestExportIdentity.cs)[:16]>/assetripper-source-1ac666f.tar.gz`; source acquisition marker `quest-source-acquisition.json`, instrumented source and `quest-identity-tool.json`. | `build_tool()` calls `acquire_source()` before checking its completed build receipt. Keep the verified archive even when the exporter DLL already exists. The old uninstrumented GUI release downloader is a different bounded recovery path; shipping that GUI alone does not satisfy full recovery. |
| NuGet, including exporter transitives, current mod, XR reference projects, QuestWeaver, codecs, metadata readers and Bee apphost. Sources are `api.nuget.org/v3/index.json` and `nuget.bepinex.dev/v3/index.json`; upstream exporter may declare its own graph. | Usually `%USERPROFILE%/.nuget/packages` / `~/.nuget/packages`, unless overridden by NuGet settings. Project `obj/project.assets.json` contains resolved paths. Particular tool caches can skip a build once their exact receipt qualifies. | Several `dotnet build`, `run` and `publish` calls implicitly restore. The mod uses floating `BepInEx.Analyzers 1.*`, `BepInEx.Core 5.*`, `BepInEx.PluginInfoProps 2.*`. One cached package of each name is not a complete or reproducible restore proof. Pin the release's resolved transitive closure in a private generated lock/config, preserve ordinary desktop project behavior, restore from a local feed and then build with `--no-restore`. Disable network-dependent NuGet audit only for the explicit offline build environment. |
| Portable audio/texture codecs. `NativePortableRecovery` references AssetRipper.TextureDecoder 2.6.3 and Fmod5Sharp 3.1.0 with a checked transitive lock. | `B/tool-cache/campaign-native-codecs/native-codecs-<sourceFingerprint[:16]>/codec-build.json`; matching executable file receipt skips rebuild. Full recovery has other private tool roots. | A missing codec receipt invokes a locked NuGet restore, which still needs all package files locally. `RestoreLockedMode=true` prevents version drift; it does not make an online feed local. `ManagedInventory` also calls `dotnet build` without a dedicated whole-tool skip. |
| Official TMP 3.0.6 source archive: `https://packages.unity.com/com.unity.textmeshpro/-/com.unity.textmeshpro-3.0.6.tgz`. | `B/tool-cache/official-tmp/com.unity.textmeshpro-3.0.6.tgz`. `download_sdk()` uses the verified existing archive without HTTP. | This independent shader-source archive is required even if a UPM package with the same name has already been installed elsewhere. An offline pack must map it to this consumer. |
| Legacy post-effect sources from Unity Standard Assets 5.3.5f1 official `download.unity3d.com/.../MacStandardAssetsInstaller/StandardAssets.pkg`. The installer is treated only as a source archive, not executed. | `B/tool-cache/legacy-post-effects/unity-standard-assets-5.3.5f1/`. All three pinned sources (`BlendForBloom.shader`, `BrightPassFilter2.shader`, `BlurAndFlares.shader`) skip HTTP. Otherwise a retained verified `StandardAssets.pkg` allows local extraction. | Preserve the private source cache; a prepared source dependency pack could avoid fetching the much larger original archive if its individual source redistribution terms are resolved. Existing project output alone is not this acquisition cache. |
| Proton ARM64EC/FEX procedural runtime and notices. Pinned GameNative GitHub release `.wcp`, FEX Launchpad `.deb` and three `raw.githubusercontent.com` license files are in `proton.lock.json`. | `B/tool-cache/campaign-native/procedural-runtime-proton/<nativeKey>/native-build.json` skips the complete native rebuild after qualification. Otherwise shared `procedural-runtime-proton/downloads/{proton-11.0-2-arm64ec.wcp,fex-emu-wine_2609.1-1~n_arm64.deb,Wine-LGPL-2.1.txt,FEX-MIT.txt,ntsync-LGPL-3.0.txt}` files each skip acquisition when valid. | Include every notice as well as the two binary archives. The source-package/build-recipe URLs in the lock are provenance references, not extra downloads on this build path; their corresponding-source obligations still need review for a public bundle. The legacy Box64/Wine 9 backend has a different Ubuntu/archive closure and must not be treated as the current default. |
| Opus 1.5.2. Primary actual Xiph GitHub release, one HTTPS fallback through `downloads.xiph.org`; same pinned SHA-256. | `B/tool-cache/campaign-native/voice/<voiceKey>/native-voice.json` plus `libopus_egpv.so` skips the build. Otherwise `voice/<voiceKey>/verified-source/opus-1.5.2.tar.gz` supports local extraction/build. `voiceKey` binds archive, bridge, helper source, NDK and host-test mode. | There is currently no shared Opus archive above the per-key directory. Changing `native.py` can require a download despite an archive in an older key. A future offline store should resolve the immutable upstream archive by its own hash while keeping native output identities exact. |
| Passthrough OpenXR headers from Khronos OpenXR-SDK `release-1.1.63` on `raw.githubusercontent.com`. | `B/tool-cache/openxr-headers/openxr/{openxr.h,openxr_platform_defines.h}`, individually hash-pinned by `scripts/build-quest-native.py`. | Both existing valid headers avoid HTTP. NDK compilation is local. Include the complete two-file source closure, not merely a previously built `.so` under a different source key. |
| Shader/compute translation host tools. | Windows `prebuilt/quest-converters-win64-v1.zip` ships in the source archive; it contains the pinned executables, sources and notices. Linux `converters.ensure()` currently selects existing `vkd3d-compiler` 1.2 and `spirv-cross` from PATH. | No converter download is needed on the ordinary Windows build path. Linux needs separately provisioned native tools and their dynamic-library closure; the Wizard does not install them. `build_converters.py` downloads upstream sources only for the developer's converter packaging recipe, not for an ordinary Windows user. |
| Android final Gradle export and Maven dependencies generated by the actual Unity Android build. | Unity's installed Gradle/SDK tools plus the user's Gradle cache (normally `~/.gradle/caches`, including on Windows relative to its user home), and generated project/export data. | Installed Editor/NDK/JDK files alone do not prove a cold Gradle export is offline. Derive the closure from the real final generated Gradle project, retain POM/metadata/JAR/AAR/plugin artifacts and explicitly invoke offline resolution. Repository names/versions are not fully established by this source audit. |
| Offline PC profile, DLC selection and logo. Account name/ID come from local Steam records or explicit local Epic/GOG data; owned install inspection is local. Steam logo defaults to a pinned `store.akamai.steamstatic.com` PNG even for non-Steam profiles. | Per-session `W/sessions/<session>/steam-logo.png` and profile JSON, or selected local `choices.steamLogo`. | No provider HTTP login, Cloud save, profile photo or Horizon call is required. Automatic Windows Steam DLC fallback uses the already running local Steam client/SDK; use local entitlement metadata or an explicit matching DLC declaration where that client context is unavailable. A new session can still download the 2,843-byte logo unless a local choice is supplied. Bundle that exact vetted branding artifact or use a qualified shared local store to remove this unnecessary online prerequisite. |
| Wireless installation/log collection via ADB. | Existing ADB discovered by the installer, Editor SDK ADB, or owned `R/scripts/.quest-adb/platform-tools/`, with retained `platform-tools_r37.0.1-{win,linux}.zip`. | Missing ADB may download from `dl.google.com`. Wireless ADB requires local LAN reachability and authorization, **not Internet access**. Build-only mode does not need ADB. |
| Wizard images, fonts, logos, GitHub/BuyMeACoffee links. | UI images and SVG/PNG branding are shipped. All 21 promo pictures have a checked `localPath`; optional cache is `W/artwork/publisher`. | Complete current UI renders locally without fetching promo art. The background Gallery can attempt optional replacement downloads if artwork is missing; strict offline mode should disable this worker. External links are user navigation, not build prerequisites. |

Source authorities: `tools/quest-installer/{bootstrap.ps1,bootstrap.sh,
linux_bootstrap.py,adb_bootstrap.py}`, `tools/quest-wizard/{provision.py,
unity_setup.py,wizard.py,promotional.py,tools.lock.json}`, `tools/quest-builder/
{dependencies.py,builder.py,release.py,host_resources.py,campaign_native.py,
shaders.py,update_driver.py}`, `tools/quest-recovery/{export_identity.py,
portable_decoder.py,full_recovery.py,tmp_shaders.py}`, `scripts/build-quest-native.py`,
`tools/quest-procedural-runtime/{runtime.py,proton_runtime.py,proton.lock.json}`,
the selected `.csproj`/NuGet config files and both package manifests. URLs cited
in comments or provenance records are not automatically executed network calls.

## Why an installed or resumed Builder can still access the network

1. Some large stages are completed, but the next previously unexecuted producer
   can still need a new archive, notice or restore. The supplied 054817 capture
   reached the first Opus request only after twelve preparation checkpoints.
2. New Builder ZIPs use new immutable source workspaces. The public source ZIP
   deliberately omits local XR DLLs, `bin/obj`, package caches and licenses.
3. Python ABI identity contains the selected base executable path; relocating the
   extracted Builder can require a new conversion venv even with the same version.
4. Native helper/source/NDK changes select new compilation keys. Opus currently
   puts its upstream archive inside that compilation key, so a helper fix can
   leave a useful old archive inaccessible to the new key.
5. .NET build commands can restore or query floating package versions and audit
   services. Warm package files are useful, but no explicit offline configuration
   currently constrains every tool invocation.
6. Unity reimport/new project creation can resolve packages again. Gradle can
   refresh dependency metadata. Deleting shared package caches or selecting a new
   operating-system/SDK/Editor combination changes those conditions.
7. A fresh session's mandatory Steam-logo acquisition is separate from local
   identity capture; optional promotional downloads are separate again.

Completed work must continue to be reused through its existing producer receipts.
An early preflight must not hash or rescan gigabytes of game/Unity content, and
must not turn a presence hint into new authority to skip validation. A quick
network check only says that a currently missing dependency endpoint is reachable
with this client's TLS/proxy settings. It cannot promise that a later large
download, OAuth sign-in, expiring license or transitive restore will succeed.

## Unity licensing and redistribution boundary

Unity's current official Hub documentation activates **Unity Personal by signing
in to Hub**. Its offline license-request method is not available for Personal;
it is documented for eligible Enterprise/Industry seats and serial-based
licenses. The Hub's named “Work offline” checkout feature is for subscriptions
with floating licensing, not a universal Personal-mode switch. Therefore do not
promise fully disconnected fresh Personal activation or a fixed offline lifetime
for our pinned legacy Editor. On an already activated workstation, execute the
existing Editor probe with networking unavailable and report its result for that
machine. [Unity license management](https://docs.unity.com/en-us/hub/manage-license),
[Unity floating-license offline checkout](https://docs.unity.com/en-us/hub/work-offline).

A public game-free dependency pack still needs component-by-component permission,
notice and corresponding-source review. In particular, Unity Editor installation
rights and Unity Runtime distribution rights are distinct; a downloadable Editor
installer is not automatically ours to mirror or repack. Keep the Editor/Android
module installation and activation as the owner's official provisioning boundary
unless a distribution right is established. Likewise, do not infer Standard
Assets/TMP/Android SDK redistribution permission from download availability.
This audit makes no blanket legal approval of redistribution. [Unity Editor
Software Terms](https://unity.com/legal/editor-terms-of-service/software).

The offline tools pack never includes a PC game, recovered game code/assets,
generated campaign bank, saves, platform account files, license/activation files,
private signing keys or credentials. Original installation qualification remains
mandatory for full builds, mod updates and profile changes. Offline qualification
is the existing local ownership/installation hurdle, not a stronger online
entitlement service or an anti-piracy guarantee.

## Implementable options

### A. Prepare this workstation once

Add a separate **Prepare build dependencies** Wizard operation. It downloads and
qualifies only missing tools, upstream archives, exact package closures and
notices; it does not snapshot or convert the original game, import all game
assets, run an exhaustive shader matrix or build an APK. It uses the same local
version/source selectors as the actual build so progress cannot promise a
different environment. Any owner-specific Unity activation remains visible and
can be completed before departure from the network.

Use a stable private store with immutable archive keys, separate from native
output keys and per-session source workspaces. Prepare the exact pip ABI,
NuGet graph, UPM graph and Gradle graph, and store a small manifest of dependency
IDs, versions, source hashes, host ABI, consumer modes and qualified artifacts.
Keep this store across normal source ZIP replacement and let storage cleanup
state clearly when removal disables offline readiness.

Once all prerequisites are qualified, an explicit offline mode uses only local
resolution: pip `--no-index --find-links`, a private NuGet config/feed plus exact
locks and `--no-restore` for later builds, local/embedded UPM dependencies, and
Gradle `--offline`. Missing artifacts fail before original conversion with a
specific item and “Prepare dependencies” action; they never silently trigger a
download inside the long build. Offline mode must also disable optional gallery
fetches and unsolicited update/network discovery.

This is the least disruptive way to deliver the user's offline goal. [pip local
package installation](https://pip.pypa.io/en/stable/user_guide/#installing-from-local-packages),
[NuGet restore behavior](https://learn.microsoft.com/en-us/nuget/consume-packages/package-restore),
[local NuGet feeds](https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds).

### B. Separate offline dependency archive

Reuse the same dependency-store manifest to export/import an OS-specific archive
that can be copied by USB or downloaded in advance. The owner can create a
private pack from their own prepared workstation; a public pack may contain
only components whose redistribution was individually qualified. Supply Windows
x64 and Linux x86_64 variants; Python ABI-specific wheels and platform native
tools cannot be mixed. The root source ZIP stays small and changes independently.

Import validates archive ownership, paths, hashes and source/ABI compatibility
once, then writes readiness receipts. It reconstructs venvs and generated build
projects instead of copying their machine-specific absolute paths. Exporting all
of `W`, `.nuget`, `.gradle` or Unity's user folders is inappropriate: it is large,
can contain unrelated inputs/credentials, and does not produce a bounded graph.

Unity supports offline reuse through its registry cache and local folder/tarball
package dependencies; include full transitive metadata, not only a directory
named after the direct package. Gradle's offline mode similarly needs both
artifacts and dependency metadata. [Unity 2021.3 global package cache](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-cache.html),
[Unity local package paths](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-localpath.html),
[Gradle dependency caching and offline mode](https://docs.gradle.org/current/userguide/dependency_caching.html#sec:offline-mode).

The archive solves missing dependencies, not the independent Unity activation
prerequisite. A fresh disconnected Personal machine remains unsupported until
an official, applicable activation route has been established.

### C. Profile-only APK updates first

`update-profile` already skips .NET/Git/XR derivation, Unity, conversion Python,
asset export and game weaving. It needs an installed local matching PC copy,
eligible base APK, original private signing material, launcher Python, JDK 17
and Android Build Tools 35.0.0. New-session logo acquisition is the remaining
small avoidable online dependency. Prepare or import these few artifacts, reuse
or bundle the exact allowed logo bytes, and a signed profile update can be
entirely local. Installation afterward needs local ADB/LAN, not Internet.

`update-mod` is different: it recompiles current code and an IL2CPP/metadata pair,
and still needs the Unity/.NET/NuGet/UPM/native tool prerequisites. Its shorter
game-content path does not make its build toolchain intrinsically offline.

## Known sizes, and what is not yet measured

These are exact **compressed artifact bytes from current pins**, not the size
of a full offline archive, download requirement or installed disk footprint:

| Artifact | Windows bytes | Linux bytes |
| --- | ---: | ---: |
| Pinned launcher Python fallback | 15,554,015 | 35,076,205 |
| MinGit archive | 41,268,410 | System prerequisite |
| Unity Hub installer/AppImage | 193,623,232 | 204,032,504 |
| Profile-update JDK 17 archive | 190,520,081 | 192,062,472 |
| Profile-update Android Build Tools archive | 59,878,107 | 61,958,799 |
| Standalone platform-tools ADB archive | 8,044,989 | 9,054,187 |
| Legacy Standard Assets source installer | 190,062,868 | 190,062,868 |
| Proton ARM64EC `.wcp` | 98,079,159 | 98,079,159 |
| FEX `.deb` | 1,693,160 | 1,693,160 |
| Opus exact source archive | 7,839,412 | 7,839,412 |
| Mandatory default Steam-logo PNG | 2,843 | 2,843 |

The complete .NET SDK, Editor/Android modules, pip wheelhouse, NuGet exporter
closure, recovered UPM graph and Gradle graph have not been measured as one
offline pack. Do not advertise an invented total. A small mode-specific pack
for profile updates is substantially smaller in scope than the complete build
toolchain; a full pack must expose its measured download and extracted sizes
before creation/import, on the actual selected drive.

## Recommended implementation order and acceptance

1. Finish the missing-dependency-aware, fast pre-build network check. Report the
   exact endpoint family/stage and DNS/TLS/proxy failure before expensive work.
   Skip network probes for qualified local dependencies and profile-only modes
   that do not need the general compiler stack. Bootstrap failures need the
   corresponding shell-visible early message before the Wizard can launch.
2. Introduce one dependency resolver/store shared by online downloads and local
   artifacts. Reuse upstream Opus/TMP/AssetRipper archives independently of output
   compilation keys. Keep all existing completed game conversion receipts.
3. Add prepare/export/import plus explicit offline mode for profile updates.
4. Lock and cache the actual .NET/UPM/Gradle graph for a complete game build,
   including the exporter and host scheduling apphost; add OS-specific package
   resolution without changing ordinary PC-mod package behavior.
5. Test cold provisioned and resumed builds with outbound networking blocked on
   Windows and Linux, using local loopback for Wizard and local LAN for ADB.
   Include new source ZIP, changed helper, moved extraction folder, changed ABI,
   absent one notice/package/archive, failed/expired Unity activation and cache
   cleanup controls. Missing dependencies must fail visibly before conversion;
   a failed partial build must retain and reuse previously closed producers.

Readiness must be scoped to operation (`build`, `update-mod`, `update-profile`),
host/ABI, selected source requirements, actual Editor/modules and dependency
graph. A small metadata presence scan can tell the user what is likely missing;
only the producers' existing receipts and an actual disconnected execution can
establish the promised result. Do not repeat full-game hashes or import assets
merely to display an online/offline indicator.

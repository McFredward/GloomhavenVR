# Quest original network preflight (2026-10-03)

## Source-supported first route

The selected original DLLs support testing normal Photon session-code gameplay
with an offline local platform adapter before adding EOS. This is a local source
finding, not a statement that the publisher's backend accepts Android. No server
configuration, account ticket, or authentication success is invented.

The executable auditor reads the actual selected DLL bodies through a trusted
local ILSpy and attaches original file/body hashes and member line references.
Its private report currently matches all 21 scoped findings. Generated reports,
original DLLs, recovered config assets, and SDK binaries remain private.

| Original boundary | Evidence and resulting implementation requirement |
| --- | --- |
| `PlatformLayer.Init` | Calls Steam initialization and quits if unavailable. Quest must complete its own local platform startup without calling this Steam route. |
| `SM.Consoles` `PlatformConstructor.BuildPlatform` | The installed assembly always constructs `PlatformSteam`; recompiling for Android cannot change this already compiled choice. Select an explicit local provider at the Quest boundary. |
| `PlatformGeneric` | Contains local input/data/message/profanity services and optional Hydra/Pros initialization. Instantiate with Hydra and Pros disabled. Audit entitlements/DLC separately against the player's installed content. |
| `PlatformNetworking.GetCurrentUserPrivilegesAsync` | The original desktop implementation already immediately returns `Success/true`, without a provider request. Preserve this local desktop policy; do not replace it with generic social's `UnspecifiedError` callback. |
| `NetworkManager.SwitchRegion` / `JoinSession` | Select the original Photon platform/region and join the original room using a `UserToken`. The inspected join body contains no EOS/Steam-ticket call. |
| `NetworkManager.GetUserToken` | Carries original version, imported profile and crossplay flags; the inspected body requests neither a Steam ticket nor an EOS ProductUserId. Preserve the original token schema and local profanity callback completion. |
| `PhotonPlatformConfig.InitDefaults` | Reads the original `BoltRuntimeSettings` resource and sets `AuthenticationValues=null`. `PhotonClient` supplies custom auth only when non-null. Preserve this original behavior, rather than creating a new Photon app or fabricated auth. |
| `GHNetworkCallbacks.ConnectRequest` | Checks real network version, capacity, crossplay and ordinary connection tests; the inspected admission body contains no EOS/Steam ticket gate. Preserve all original checks, generated registrations and serialization. |
| `PlatformLayer.Initialize` | Unconditionally instantiates/initializes EOS at desktop startup, independently of the inspected Photon admission path. A Quest local provider must omit this unused endpoint instead of pretending EOS authorization succeeded. |
| `SaveData.InitialiseDataManagers` | Persisted `Global.EpicLogin` can restart EOS authentication. Keep real save data compatible while preventing this excluded desktop login from entering Quest startup; report unavailable invite/login capability truthfully. |

The original `FFSNet.NetworkVersion.Current` is `2`. Retain the original
assemblies and protocol-generated `bolt.user.dll`; do not substitute Unity's
APK version `0.1.0`, the mod build number, or `DevVersion`. The imported Steam
name/full ID/account ID remain display/save identity and provide no ticket.

These observations cover the named startup/token/admission members and the
default Photon transport. They are not a complete call-graph proof or a console
compatibility proof. The publisher can change Photon application admission
policy independently of the installed client. Photon documents that service
admission depends on its application authentication configuration; default
client auth is insufficient to infer that policy. [Photon custom authentication](https://doc.photonengine.com/bolt/current/connection-and-authentication/custom-authentication)

## B612 identity and DLC admission proof

Read-only review of the selected original DLLs and the startup adapter frozen at
`fb59d91d` confirms the following. This note changes no platform label, token,
admission rule or runtime source. The startup target remains diagnostic with
`fullGameReady=false`; no Android game-room admission has been demonstrated.

`PlatformLayer.PlatformID` is originally the constant `"Steam"`; selecting
`PlatformGeneric` does not change that separate getter. The current adapter
preserves it. Unchanged `NetworkManager.GetUserToken` therefore emits the Steam
label, baked profile name/full Steam ID, and the baked account ID through
`PlatformUserData.PlatformNetworkAccountPlayerID => PlatformAccountID`. It keeps
the original network version, build type, crossplay flag and serialization.
Separately, `NetworkUtils.PlatformType` and session negotiation remain `PC=1`.
These are client identity metadata, not proof of a live Steam login.

The actual host admission method is `GHNetworkCallbacks.ConnectRequest`:

| Host setting | Original platform admission condition |
| --- | --- |
| Crossplay enabled | The client's crossplay flag must also be enabled; no platform-name whitelist is applied. |
| Crossplay disabled | `PlatformLayer.MatchesCurrentPlatform` must pass. Original standalone desktop builds recognize `Standalone`, `Steam`, `GoGGalaxy`, and `EpicGamesStore` as the same PC group. An arbitrary `Quest`/`Generic` label is outside that group. |

Version, capacity, password and existing game/save checks still apply. The named
token/admission bodies contain no required Steam ticket, EOS authorization or
live account-validity request. Original desktop privilege/user-permission
callbacks already implement local policy; this is not permission to fabricate
provider authorization. A later label review, if needed, belongs at the
`PlatformLayer.PlatformID` metadata getter, leaving protected networking and
tokens untouched. The existing desktop Photon smoke reached only master/lobby.

The four `PlatformGeneric` constructor flags are `initHydra`,
`initEntitlements`, `initPros`, and `isDevicePairingIncluded`. The current
`false,true,false,false` creates an empty `PlatformEntitlementGeneric` ownership
set. Its refresh only initializes and invokes the callback, satisfying native
startup without granting DLC or consulting a store. The separate game
`PlatformDLC.UserInstalledDLC` returns false when Steam is uninitialized;
`CanPlayDLC` requires that check **and** the ruleset file. Supported entitlement
names and installed DLC bytes do not establish ownership.

For a later base-only playable target, explicitly keep the local DLC mask at
`None` and reject every unsupported nonzero/unknown mask. Preserve
`SaveData.LoadCampaignMode`'s `CanPlayPartyData` check and the original client's
`GameToken.DLCFlag`/`GetInvalidDLCs` rejection; do not strip DLC flags from PC
saves or host tokens to make them load. Any later DLC support needs independent
PC ownership evidence and matching portable content, with unknown ownership
failing closed and no live store calls on Quest. Shared menu/promotion assets
and localization can be loaded independently of DLC ownership, so their
presence is neither a license nor proof of playable DLC.

Member references in the read-only `decompiled/GH.Runtime` tree:
`PlatformLayer.cs:141,246,392`; `PlatformUserData.cs:106`;
`FFSNet/NetworkManager.cs:443`; `FFSNet/NetworkUtils.cs:8,26`;
`GHNetworkCallbacks.cs:210`; `PlatformNetworking.cs:283,406`;
`PlatformDLC.cs:17,27,61,75,94`; `SaveData.cs:208`;
`GHClientCallbacks.cs:337`; `SceneController.cs:603,854`.
Scoped original `SM.Consoles` inspection verifies
`Platforms.Generic.PlatformGeneric`'s constructor and
`Platforms.Generic.PlatformEntitlementGeneric`'s empty set/refresh behavior.
`Script.PlatformLayer.GHEntitlementsProvider` exposes supported PS4/PS5 content,
not the generic ownership set.

## Exact local configuration and transport inputs

Recover `Assets/Resources/BoltRuntimeSettings.asset` from the player's original
assets and retain its genuine Photon app ID privately. The current recovered
asset has punch-through disabled, cloud region index 14, room create timeout
20 seconds, room join timeout 30 seconds, packet size 1200, 60 simulation frames
per second, IPv6 disabled, and manual server admission. The original manager
selects US when hosting, and uses the invite code prefix when joining. Preserve
the original region table/code mapping, serialization protocol and config.

Keep these original managed network assemblies and their reflected constructors,
members and generated types reachable to IL2CPP:

```text
GH.Runtime.dll, GH.Shared.dll, SM.Consoles.dll
bolt.dll, bolt.user.dll, PhotonBolt.dll
udpkit.dll, udpkit.common.dll, udpkit.platform.dotnet.dll,
udpkit.platform.photon.dll, PhotonRealtime.dll, Photon3Unity3D.dll
```

`PhotonPlatformConfig` reflects the Bolt settings/type members. The installed
`PhotonRealtime.ConfigUnitySockets` selects managed `SocketUdpAsync` and
`SocketTcpAsync`. The installed `PhotonPeer.InitDatagramEncryption` falls back
to managed `EncryptorNet`. The extra desktop-source `PhotonSocketPlugin` and
`PhotonEncryptorPlugin` P/Invokes therefore do not establish a mandatory native
dependency of this default route. Android DNS, UDP/TCP, background acknowledge
thread, encryption initialization, suspend/resume and IL2CPP metadata still need
actual runtime validation. Android manifest internet access is required.

Voice is a separate test: original `PhotonVoice.dll` / `PhotonVoice.API.dll`,
matching voice-room setup, microphone permission/capture and ARM64 codec/audio
dependencies must be investigated before claiming voice parity. Missing voice
libraries must not be silently described as working voice or used as evidence
that gameplay socket connection itself failed.

No `appclientconfig` file was found among the currently selected original local
inputs. The auditor accepts an explicit existing `--appclient-config` as a
hash-only private input; it does not create one or infer auth from its filename.

## Minimal original connect and join experiment

1. Finish original menu/provider startup with the local imported profile,
   genuine original resources and no Steam/EOS/Hydra live startup. Complete
   original profanity and multiplayer privilege callbacks with their existing
   desktop semantics. Keep unsupported social/invite capabilities unavailable.
2. On an unmodified PC game with the same real network version, enable crossplay
   and host a base-game campaign at a safe town/start-of-round state. Record its
   ordinary session code privately; do not join unrelated public rooms.
3. On Quest invoke the original session-code entry and original `JoinSession`.
   Capture distinct stages: local provider readiness, Photon initialization/
   authentication, region/room lookup, host token admission, initial save
   transfer and character/lobby readiness. Record errors and bounded progress,
   never account tickets, config values or entire backend response payloads.
4. Preserve the original host's returned `GameToken`, DLC/version/ruleset/save
   checks and serialization. If content is not yet portable, stop at the first
   genuine unsupported stage and retain the successful preceding stages as
   narrower evidence. Connection alone is not playable campaign acceptance.
5. After a representative campaign scenario exists, perform character assignment,
   ability selection, multiple rounds, door opening, town return and reconnect.
   Repeat Quest hosting and both unmodified/modded PC guests. Keep the existing
   mod same-build guard and side-channel protocol untouched.

An optional earlier desktop Unity smoke may connect the original Photon client
with original settings and no EOS, without entering another player's room.
Any success there proves this host/client configuration only; Android must repeat
the actual operation. The source audit deliberately never sets runtime success
fields from its own pattern matches.

The first real desktop smoke on 2026-10-03 succeeded. Unity 2021.3.5f1 on
`LinuxEditor` invoked the original `PhotonPlatformConfig` and original internal
`PhotonClient`, with null custom auth and the original manager's US default
region. It reached the original master/default lobby in 1.3077 seconds and
exited zero after original client cleanup. No EOS initialization or game room
join occurred. The private receipt
`editor-smoke/unity-photon-8rvwo979/smoke-report.json` has SHA256
`dca8bad1da12bf41509d66429ee02260dcd71736c792e791916cbc01b7c936a6`.
This demonstrates backend acceptance of this desktop configuration without EOS
at that time. It proves neither an Android connection nor original host token/
save admission. The repeatable command is `scripts/quest-network-smoke.py`;
automated audit/orchestrator controls pass 30 tests, separate from the live run.

## EOS contingency only if a real required stage proves necessary

The installed PlayEveryWare wrapper reports version `2.3.3`; its selected
`Epic.OnlineServices.Config` and EOS manager hardcode Windows library names.
Copying the Windows DLL or renaming an ARM64 SO is insufficient. The matching
upstream tag is pinned to commit
`b10ee579e6b3ddf30cf73a05ea1d20d2fe75129f`. Its Android implementation supplies
platform init options, Android activity/context initialization and different
binding selection. [Pinned Android implementation](https://github.com/EOS-Contrib/eos_plugin_for_unity/blob/b10ee579e6b3ddf30cf73a05ea1d20d2fe75129f/Assets/Plugins/Android/EOSManager_Android.cs),
[pinned binding selection](https://github.com/EOS-Contrib/eos_plugin_for_unity/blob/b10ee579e6b3ddf30cf73a05ea1d20d2fe75129f/Assets/Plugins/Source/EOS_SDK/Core/Config.cs)

The same-version upstream ARM64 static-C++ candidate was inspected privately:
`PlatformSpecificAssets/EOS/Android/static-stdc++/libs/arm64-v8a/libEOSSDK.so`,
23,133,440 bytes, ELF64 little-endian AArch64, SHA256
`34bf9ce7bc1047873d15712d1ac42ce7c9e1b3f7248f336c4195cf2d5e6e5b1f`.
Its dynamic dependencies are Android system libraries `libGLESv3`, `libEGL`,
`libandroid`, `libOpenSLES`, `libc`, `libdl`, `liblog`, `libm`, and `libz`.
This is architecture evidence, not proven ABI/login compatibility with the game.

The matching `eos-sdk.aar`, Unity Android helper AAR/native helper, Android config
and dependency library/Gradle settings are also required by that upstream route;
the SO alone does not provide browser/activity callback handling. Resolve only
the required Android dependencies and permissions rather than blindly enabling
its legacy storage/audio permissions. [Pinned Android setup](https://github.com/EOS-Contrib/eos_plugin_for_unity/blob/b10ee579e6b3ddf30cf73a05ea1d20d2fe75129f/docs/android/readme_android.md)

Original EOS config must stay private and retain the player's selected original
product/sandbox/deployment/client settings. The original login flow falls back
from persistent auth to Account Portal, then Connect login; this is a legitimate
candidate only if that product accepts it on Android. Test cancellation,
expiration and activity resume, and minimize scopes to proven necessary
multiplayer operations. The original EOS invite service translates `PHOTONKEY`
to a Photon session code; this does not establish that EOS is required for direct
session-code joining. No imported Steam ID, launcher exchange code, device guest,
forced authorization flag or fabricated token substitutes for backend acceptance.

Hydra's installed Steam analytics implementation requests a live Steam auth
ticket. It is not a viable offline Quest authentication route and remains disabled
at the local platform boundary. No Horizon, Steam, store-profile/friends/overlay,
cloud-save, or Hydra credential route is included in the first connect proof.

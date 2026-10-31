# Quest full-game network boundary

Implementation checkpoint, 2026-10-05. This applies only to the generated full-game Quest target. Original PC assemblies and the shared desktop mod remain inputs, never edited outputs.

## Preserved transport and identity

The supplied game uses Photon/Bolt for its session-code multiplayer route. Its original `NetworkManager`, both native network callback classes, generated Bolt states, token registration, session admission, game/save exchange and mod side channel remain unchanged. The wire version comes from `FFSNet.NetworkVersion.Current` (`"2"` in this input), not `Application.version` or the Quest APK version.

The builder-provided local profile remains the platform adapter's responsibility. It is a local display/compatibility profile; it must not become a fabricated platform authentication ticket. The original null-custom-auth Photon route is retained. An earlier original desktop master/lobby smoke proves that route can reach the configured backend without EOS, but does not prove admission from a Quest to a PC room. No unrelated existing room is joined by the validation.

Provider friends, purchases, cloud saves and Epic invitations remain excluded. A PC save can retain `GlobalData.EpicLogin=true`. The generated `UIMultiplayerEscSubmenu.Show` now requires both that saved flag and the actual `PlatformNetworking.EPICInvitesSupported` capability before displaying the Epic invitation panel. This keeps imported save bytes and original multiplayer continuation intact.

## Native voice implementation

The original Photon Voice SDK loads `opus_egpv`. Its Windows codec binary cannot load on Android. `tools/quest-network/native.py` builds official Xiph Opus 1.5.2 for the selected Unity Android NDK, ARM64/API 29. The source archive and wrapper source are checksummed, extraction rejects nonlocal members, and the cache recipe includes the helper, wrapper and NDK version. The actual built ELF and its sixteen required codec/CTL exports are audited before its receipt is written.

The publisher's managed Opus wrapper declares four fixed managed signatures against native variadic `opus_encoder_ctl`/`opus_decoder_ctl`. ARM64 requires a matching fixed native ABI. `opus_bridge.c` exports four fixed wrappers, and `StandaloneNetwork.Apply` rebinds exactly those four P/Invoke entrypoints in the generated `PhotonVoice.API.dll`. The original encode/decode APIs, codec classes, voice framing, room callbacks and original `BoltVoiceBridge.SetupAndConnect` stay intact.

The original supplied SDK's Photon microphone factory selects a Windows-only audio provider. Its genuine Unity microphone route is already present. At original voice opt-in, `QuestGameNetwork.BeginVoice` obtains Android microphone permission through real permission callbacks, selects `Recorder.MicType.Unity`, disables the Windows fallback and enables the original recorder's pause handling before invoking the original bridge. Denied permission or a missing codec/recorder does not report success or create a connection. The original eager startup permission request is moved to opt-in. The original platform voice privilege callbacks remain unchanged.

This uses Xiph's BSD-licensed source, not a downloaded proprietary Photon SDK. Include the generated `OPUS-LICENSE.txt` with the artifact. Photon documents Unity microphone capture and Android/IL2CPP support in its [Recorder documentation](https://doc.photonengine.com/voice/v2/getting-started/recorder), [Voice introduction](https://doc.photonengine.com/voice/v2/getting-started/voice-intro), and [Android ARM64 known issues](https://doc.photonengine.com/voice/v2/troubleshooting/known-issues). The pinned archive checksum comes from the official [Opus 1.5.2 release](https://opus-codec.org/release/stable/2024/04/12/libopus-1_5_2.html). Opus interoperability relies on the published [codec standards](https://opus-codec.org/docs/); its redistribution requirements are described in the [license](https://opus-codec.org/license/).

## IL2CPP cold paths and lifecycle

Preserve all fourteen assemblies named by `StandaloneNetwork.PreservedAssemblies`, including reflected Bolt startup, generated state metadata singletons, registered token constructors, managed socket constructors and Photon Voice generic decoders.

The actual original Photon Protocol16/18 readers use `MakeGenericType` and `Activator.CreateInstance` for dictionaries. Metadata preservation alone does not instantiate value-type generic methods. `QuestNetworkAot` provides explicit concrete roots and actual original serializer roundtrips for the built-in key/value layouts supported by this SDK: 99 Protocol16 cases and 152 Protocol18 cases. Protocol16's original rejection of typed array/dictionary value headers remains unchanged. This does not promise support for arbitrary hypothetical typed dictionaries registered by future third-party mods; current original game types, builtin Photon layouts and the raw mod token byte channel are the supported closure.

`QuestGameNetwork` additionally runs real local `UserToken`, `GameToken` and `CustomDataToken` roundtrips and real original Opus encode/decode in the Android player before external use. These private test buffers never alter a room, save, profile, registration or authoritative gameplay. At Debug it also tests the 251 original typed dictionary layouts and actual original managed payload encryption. Successful local checks are evidence about the built player, not a claim about server admission.

The original Photon client selects payload encryption. Its original managed Diffie-Hellman/Rijndael provider passes a two-way encrypted roundtrip. The optional `EncryptorNet` datagram fallback in this publisher's DLL contains `NotImplementedException`; it is not represented as a working fallback and the original negotiated mode is not changed.

The selected Unity 2021.3.5 IL2CPP implementation supports `Thread.Abort` through `Thread::RequestAbort`; no speculative Photon worker rewrite was made. Original background acknowledgements, Bolt timeouts and connection-loss callbacks remain owners of gameplay transport. Quest app pause/resume is logged once per transition, `Application.runInBackground` is enabled for this target, and the original recorder stops/resumes capture on pause. No second gameplay connection or fabricated reconnect is introduced.

## Integration contract

Root integration owns the full-target dispatch:

1. Load `PhotonVoice.API.dll` alongside the game. On full target only call `StandaloneNetwork.Apply(game, voiceApi, report)` and add its returned two game type names to the changed-type allowlist. Write/deploy the adapted voice API. Skip the startup-only replacement of `VoiceChat.VoceChatOptions.SwitchStatus`; the full target keeps the original voice UI/controller.
2. Preserve the fourteen `PreservedAssemblies` in `link.xml`. Include both `QuestGameNetwork*.cs` sources and add `QuestGameNetwork` to the full-target bootstrap. Its initialization waits for the real `QuestStandalonePlatform.Enabled` boundary.
3. Full target must not apply `QuestStandaloneScope`'s startup-only voice-unavailable override when `QuestGameNetwork.NativeVoiceAvailable` is true. Missing native codec remains a real bounded failure, not a silently simulated voice service.
4. Call Python `native.py.stage(output_cache, ndk, android_plugin_directory, notices_directory)` and retain its returned paths and checksums. It builds/copies `libopus_egpv.so` into Android ARM64 plugins and the required `OPUS-LICENSE.txt` into packaged notices/materials. `build_network_native(output_cache, ndk)` remains available for validation/cache preparation. Install `tools/quest-network/requirements.txt` into the builder venv: pinned CMake 3.31.6 and Ninja 1.11.1.4 with official [CMake wheel hashes](https://pypi.org/project/cmake/3.31.6/) and [Ninja wheel hashes](https://pypi.org/project/ninja/1.11.1.4/). The ADB installer remains standard-library-only. The helper does not install packages globally.
5. Manifest permissions are `INTERNET` and `RECORD_AUDIO`. Runtime microphone permission is requested only on voice opt-in. Keep Android ARM64 plugin import settings and existing signing/application identity under the root builder.

`NativeVoiceAvailable`, `LocalSerializationReady` and `LastState` expose bounded runtime evidence. Existing native and mod logs remain authoritative for actual connection failures.

## Validation and limits

`tests/QuestNetwork.RuntimeCompile` compiles the actual runtime sources against the genuine Unity 2021.3.5 API and imported original game/voice assemblies. It takes `QuestNetworkPluginsRoot` and `UnityManagedRoot` MSBuild properties. The focused compile passes without warnings or errors.

`tests/QuestNetwork.Tests` is a standalone executable; the root test entrypoint need not be edited to run it. Arguments are original managed directory, Linux host Opus plugin, private proof directory and optional `QuestNetwork.RuntimeCompile.dll`. The current run passes 12,000 assertions, including:

- unchanged unrelated game types and protected gameplay/network methods;
- original Bolt reflected startup/state/socket/token metadata and absence of Reflection.Emit in the retained network SDK closure;
- original-to-generated and generated-to-original admission tokens, save/session/DLC tokens, Unicode profile fields and raw save/mod payload framing;
- twelve actual original SDK codec paths across 8/16/48 kHz, mono/stereo and float/short PCM, including fixed CTL get/set and non-silent decoded signal;
- all 251 original typed Photon dictionary roundtrips and original two-way payload crypto;
- unchanged checksums for all fourteen canonical original input DLLs.

The private `original-network-codec.json` receipt records the actual inputs and explicitly leaves Android connection, PC room admission and hardware voice proof false. Host .NET BinaryFormatter tests do not prove Unity PC/Android save compatibility by themselves. The actual player cold-path checks, stripped DLL/native generic code audit, campaign save exchange, PC-to-Quest/Quest-to-PC admission, modded/unmodded gameplay and bidirectional voice must be evaluated with the integrated APK and hardware capture. Keep those distinctions in release and hardware-test notes.

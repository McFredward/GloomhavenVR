# Original Campaign portability audit

These tools read the legally supplied original assemblies and the private recovered
Unity project. They never rewrite those inputs or distribute original DLLs/assets.
Run from an isolated worker checkout. Store generated inventories and fixtures in
a private cache outside the original/recovered trees.

## Evidence collected on 2026-10-05

The source snapshot contains 71 custom managed assemblies, 3,025 P/Invoke imports
and 684,586 direct IL call/function-pointer edges. An IL edge is source evidence;
virtual dispatch, reflection and serialized scene roots need separate examination.
The component census checked 31,207 recovered prefab/scene/asset files using both
the original script file ID and its recovered DLL GUID.

| Runtime boundary | Original evidence | Quest action |
|---|---|---|
| Apparance | Campaign genuinely creates dynamic native entities, not only fixed scenario geometry. | Original DLL retained behind the separate copied-buffer ABI worker. See `../quest-procedural-runtime/README.md`. |
| InControl | Live manager in `Intro`; native manager rejects Android before `Native.Init`/version import. XInput manager rejects platforms outside Windows before imports. | Preserve original managed input manager and existing Quest VR input. No blanket removal. |
| AVPro Movie Capture | All AVPro script identities have zero references in the recovered assets; no external game IL calls into capture classes. `CaptureBase.Awake` itself has unguarded Windows native calls. | Do not instantiate capture components. This dormant recording SDK does not render the intro/preview. |
| Alembic | All identities in `Unity.Formats.Alembic.Runtime` have zero serialized references; no external game calls found. | No live Campaign dependency demonstrated. Do not pretend the `abci` native plug-in was ported. |
| Voice `AudioIn` | Original desktop recorder can choose Windows capture. Original game has one live `VoiceChat.BoltVoiceBridge`. | Existing Quest seam selects original Unity microphone provider and permissions; native Opus decoder/encoder retained. |
| Voice WebRTC | `WebRtcAudioDsp` has zero serialized references and no game construction found. Its SDK native calls are real but belong to that component. | Original game does not require this extra desktop DSP library on the audited path. |
| Windows console | `Photon.Bolt.ConsoleWriter.Open/Close` contains Win32 imports, but no calls into that utility exist in the original IL inventory. The active `BoltLog` console writer is a different managed nested class. | Preserve SDK; do not disable multiplayer logging. |
| EOS dynamic library loader | `SystemDynamicLibrary`/`SystemMemory` and the x64 rendering plug-in are EOS wrapper dependencies. | Existing platform service seam avoids store/EOS initialization when not required; Photon gameplay transport remains. |
| Embedded Photon native socket/encryptor helpers | Optional helpers live in `GH.Runtime`; original gameplay/voice transport uses the separate Photon managed SDK. No game construction of the native socket helper was found. | Keep the tested managed original transport/crypto selection. Hardware handshake remains separate evidence. |

Zero serialized references alone does not prove an SDK can never run. The relevant
rows above also inspected external calls/construction and actual platform guards.
Future game versions must be re-audited; this report is not permission to strip
unfamiliar types globally.

## Scene identities

Original bootstrap, intro, main-menu and scenario transitions call Unity scene APIs
with scene **names**. `SceneController` enumerates build paths to collect names, and
unloads `InitialLoadingScreen` by its name. The extra Quest wrapper scene does not
require shifting those original calls.

Original Bolt scene indices come from `BoltScenes_Internal`'s native name/hash
mapping rather than Unity build indices. Preserve those IDs for PC compatibility.
The observed `Scene.get_buildIndex` uses are LeanTween's level-loaded callback and
diagnostic information; the original Campaign transport does not use a Unity
build-index offset as its protocol scene ID. Editor/reset sample utilities are
not evidence of the Campaign startup flow.

## Original initial Addressables labels and lifetime

The serialized `AssetBundleManager` has its original two providers:
`StandaloneLabelProvider` and `Script.AssetBundleLoading.HighShadersLabelProvider`.
The manager starts with `always_loaded_base`, adds provider labels, then adds owned
DLC labels according to the original DLC flags. It calls
`Addressables.LoadAssetsAsync<UnityEngine.Object>(label, null)` sequentially.

| Label | Original Unity objects | Non-Unity catalog value locations | Source bundle groups used by objects |
|---|---:|---:|---:|
| `always_loaded_base` | 300 | 620 | 6 |
| `always_loaded_standalone` | 114 | 684 | 1 |
| `always_loaded_base_high` | 15 | 0 | 1 |
| `always_loaded_dlc_1` | 475 | 33 | 1 |
| `always_loaded_dlc_2` | 130 | 0 | 1 |

The original `ResourceLocationMap.Locate` uses `requestedType.IsAssignableFrom`
against each location's resource type. Enum/serialized field/value locations are
therefore excluded by the original `UnityEngine.Object` request too. The original
`BundledAssetProvider` calls Unity's typed `LoadAssetAsync`/subasset APIs, not an
independent scalar serializer. Keeping exact recovered native objects and their
serialized fields preserves these values; fabricating separate scalar Unity assets
would change the original semantics.

`_alwaysloadedHandles` stays pinned for the original manager's session. Original
scenario changes release `_loadedHandles` and `_instantiateHandles` only. Current
`QuestGameAddressables.Install` initializes the Android catalog, registers typed
aliases and checks label locations without performing another asset preload. Its
initialization handle remains retained. No source-proven second full Campaign
preload or new object lifetime owner exists in that wrapper.

The original provider adds `always_loaded_standalone` when the game setting's
device type is Standalone; high shaders are added unless `DoNotLoadHighShaders`.
These settings, not `Application.platform` alone, govern these two labels. Native
memory/VR frame cost must still be measured on the headset after loading scenarios.

## Runtime code generation and original save format

No Reflection.Emit or expression compilation call was found in `GH.Runtime`,
`ScenarioRuleLibrary`, `MapRuleLibrary`, `GH.Runtime.FirstPass`, `ThirdParty`,
InControl or the original Photon transport/voice assemblies.

The actual precompiled Newtonsoft assembly already reports
`JsonTypeReflector.DynamicCodeGeneration == false` and uses
`LateBoundReflectionDelegateFactory`. Its assembly-name lookup in the type binder
is not dynamic bytecode generation. The two Addressables/resource-manager
`Assembly.Load(string)` paths resolve serialized managed type names; they do not
load runtime-generated DLL bytes. There is no YamlDotNet assembly or namespace in
this supplied managed snapshot.

Odin was a real blocker: desktop `EmitUtilities.CanEmit` returns constant true.
The Quest weaver selects its original AOT formatter fallback and routes all 23
original member/callback helper methods through their **existing reflection
delegates**. The latter is necessary because `BaseFormatter<T>.CreateCallback`
calls emit helpers directly. Only changing `CanEmit` leaves those managed
callbacks emitting DynamicMethod code.

The host proof invokes actual original `GlobalData` constructors,
`ISerializable.GetObjectData`, deserialization constructors and native
`[OnDeserialized]`/`ValidateAllAdventures` callbacks, using the original Odin
`BinaryDataWriter` and reader. Fifty native save members are retained. Original
PC and adapted reflection paths produce identical bytes and read each other's
streams for default and nonempty settings, key bindings, enum lists, user/tutorial
strings and nested stats dictionaries. It also poisons all **345 original Odin
runtime emit API sites**: the Quest fixtures still pass while the desktop selector
fails the explicit negative control.

The test uses six explicit host-only Unity native getter/logger substitutes and
disposable original platform/save singletons. Original game bodies are unchanged.
Global fixtures keep Campaign/party lists empty deliberately. A supplemental oracle
now also roundtrips nonempty original `List<PartyAdventureData>` slot metadata and
`List<GHRuleset>` metadata, including native owner/avatar bytes, independent item
flags, character tuples, timestamp and run identity. Original constructors and
`RefreshCheckpoints` run against the disposable root. This is slot metadata rather
than a `CMapState` gameplay snapshot or Android IL2CPP proof.

Observed concrete strong formatter/serializer roots include:

- `ListFormatter<CombatLogFilter>` and `EnumSerializer<CombatLogFilter>`.
- `DictionaryFormatter<string,int>` for original nested stats.
- `ListFormatter<string>`, `ListFormatter<GlobalData.KeyBinding>`,
  `ListFormatter<PartyAdventureData>` and `ListFormatter<GHRuleset>`.
- Strong `ComplexTypeSerializer<GlobalData.KeyBinding/PartyAdventureData/GHRuleset>`.

The list/dictionary static constructors also reference `ListFormatter<int>` and
`DictionaryFormatter<int,string>`. Preserving open generic metadata alone does not
compile concrete value-type generic bodies; the Quest runtime supplies real typed
AOT roots. The proof writes its observed cache/type closure to private text files.
It now captures `StrongTypeFormatterMap` and `WeakTypeFormatterMap` too, since
`FormatterInstances` alone misses directly returned reflection/ISerializable
formatters. See [the exact additional AOT roots](Odin-AOT-CLOSURE.md).

Manatee.Json's 106 emit sites belong to interface type synthesis used by its
fallback resolver. The sole original game client is the optional error-report
Trello path. Trello's original `DefaultJsonSerializer.InitializeAbstractionMap`
registers concrete mappings for its JSON interfaces; ordinary Campaign rules,
save and multiplayer do not call this generator. Arbitrary unmapped-interface
JSON deserialization remains an unsupported AOT SDK path, not evidence to disable
the game's error UI or its original mapped serializer.

## Clearly marked dummy identity

Original `GHNetworkCallbacks.ConnectRequest` rejects null tokens, incompatible
versions/crossplay settings, session limits and original basic admission failures.
`FFSNet.NetworkCallbacks.PassesBasicConnectionTests` checks capacity, password and
its original username blocklist. Neither rejects a player/account string of `0`.
The original `PlatformUserData` itself returns Steam/account `0` when Steam is
unavailable. Retain that honest identity for the marked local hardware APK; do not
invent a Steam account or login token.

Save association/reconnect code compares these IDs. Two identical dummy builds
share an identity and cannot establish distinct-account behavior. A single dummy
Quest client with a real PC account still tests the actual admission and protocol
boundary. Owner builds use their own original profile metadata as specified.

## Running the focused checks

```sh
dotnet run --project tools/quest-campaign-audit/QuestCampaignAudit.csproj -- \
  /private/owner-game/GH_Data/Managed /private/cache/original-inventory.json
python3 tools/quest-campaign-audit/component_census.py \
  /private/recovered-campaign /private/cache/component-census.json
dotnet run --project tools/quest-campaign-audit/tests/AuditTests.csproj -- "$PWD"
python3 -m unittest discover -s tools/quest-campaign-audit/tests -p 'test_*.py'
python3 tools/quest-campaign-audit/run_odin_proof.py \
  --managed /private/owner-game/GH_Data/Managed \
  --unity-editor /private/Unity/Editor/Unity --cache /private/cache/odin-proof
```

The proof checks original input hashes after execution and records its host
substitutes, exact fixture hashes, negative control and hardware limits. No online
API calls, account credentials or real player save data are used by these tools.

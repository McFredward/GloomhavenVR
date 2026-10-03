# Original Quest startup boundary

Worker baseline: `6b2dbce3`, ModBuild611. This implements an explicitly scoped
original-menu diagnostic. It does not certify the original campaign, full mod,
save round trips, shader parity or multiplayer on Android.

## Generated compatibility

```
dotnet run --project tools/QuestWeaver/QuestWeaver.csproj -c Release -- \
  standalone --standalone-target startup --managed ORIGINAL_MANAGED \
  --overrides WOVEN_OUTPUT --profile quest-profile.json --output EMPTY_STAGE \
  --bepinex REAL_BEPINEX_DLL --mod CURRENT_MOD_DLL --report startup.json
```

The optional BepInEx and mod arguments must be supplied together. Use the actual
BepInEx.BaseLib5.4.20 `lib/netstandard2.0/BepInEx.dll`, never reference stubs.
`--overrides` selects existing woven copies while all other inputs come from the
owned installation. Originals are read-only. Output must be empty and separate.

The report's `startupAdapterComplete` describes generated platform adaptation;
`fullGameReady`, `modLifecycleComplete`, `eosAuthorised` and
`proceduralRuntimeAvailable` remain false. Independent `remainingGates` cannot be
cleared by a successful serialization or menu appearance.

The factory uses the original `PlatformGeneric` with Hydra/Pros disabled and
local entitlements enabled. A real original `UserDataGeneric` is created from
the embedded PC name/account because its desktop implementation otherwise only
creates users for keyboards/gamepads. Its original `IsSignedInOnline` stays
false. `PlatformUserData.IsSignedIn` means local save-owner availability here;
`PlatformLayer.IsValid` remains false, no Steam ticket is manufactured, and no
EOS authentication status is fabricated. Stored `Global.EpicLogin` is retained:
its platform request logs the unavailable offline EOS boundary. Original
SaveData, RootSaveData and PlatformFileSystem semantics are unchanged. GH.Runtime
content-path calls use persistent `quest-owned-game`; persistent save paths are
not redirected. Original protected rules/Photon/Bolt/network-manager code is
unchanged.

Global Workshop support stays false, so startup never scans or downloads
Workshop mods. Its original Extras row remains present and disabled with its
excluded callback removed. The temporary XR presentation disables the exact
serialized Guildmaster entry and displays Core/Loc's bilingual exclusion tooltip.
It preserves original native widgets, lists and ordinary pointer callbacks.
Its world-space positioning is diagnostic presentation, not full mod parity.

Original ApparanceEngine imports the unavailable Windows native engine. Only its
Awake/Start/Update/Stop/OnDestroy lifecycle callbacks are suppressed for this
menu-only target. Instance is not made ready and procedural APIs are not replaced
with fabricated results. Campaign geometry needs verified native Android support
or a separately proven prebake path. Voice/native startup remains a separate
boundary audit; no Photon or Bolt code is changed.

Deploy modified GH.Runtime.dll, SM.Consoles.dll and Apparance.Unity.dll in-place,
preserving their recovered `.meta` GUIDs. Add QuestGame.Compatibility.dll and,
when generated, the BepInEx.dll closure separately without duplicate identities.
Merge generated linker preservation with the hook integration link.xml.

## Original BepInEx implementation closure

The adapter reads the actual framework and current mod's BepInEx member
references. Original configuration methods are preserved conservatively because
the options browser reflects over them. A transitive method/type closure retains
the original ConfigFile/ConfigEntry converters, change events and logging APIs.
Unused desktop Chainloader, MonoMod, Cecil, Harmony and Emit dependencies are
removed. Unknown current-mod APIs fail precisely; generic signatures compare
parameter positions, not compiler-specific generic parameter names.

`BepInEx.QuestStandalone.Initialize(string root)` initializes Paths before any
ConfigFile type initializer. The generated BaseUnityPlugin constructor retains
original plugin metadata, PluginInfo, logger and config implementations, without
desktop Chainloader access or an Assembly.Location assumption. This generated
constructor still requires real Unity AddComponent/lifecycle evidence. It does
not activate the mod in the menu diagnostic.

The next mod activation gate is source-derived replacement of RuntimeDepsLoader's
LoadFile discovery with compiled package readiness; reuse the existing Android XR
session exactly once, preserve original module init/teardown, resolve mod content
against the extracted owned root, and verify private XR API compatibility against
the real selected packages. Native ABI and reflection/generic closure remain
independent gates. Do not enable Plugin merely because config/logging works.

## Runtime contracts and evidence

All new QuestGame runtime files require `GHVR_QUEST_STARTUP`; the lightweight
hardware probe needs no Addressables package. QuestGameBootstrap validates build
and owned ZIP manifests, reuses hash-valid files, replaces corrupt files through
verified temporary files, initializes the real native Android catalog, checks
every required label's real assets, then loads original Bootstrap. The original
Bootstrap -> Intro -> Gloomhaven_unified -> MainMenu callbacks remain original.

Resources/quest-startup-content.json is schema1 with inputKey, archive
`quest-startup-content.zip`, archiveSha256 and exact files[{path,sha256,size}].
ZIP entries match the manifest exactly; paths are relative beneath the extracted
root and symlink parents, traversal, extra/missing entries and hash mismatch fail.

Resources/quest-startup-addressables.json is schema1 with matching inputKey,
runtimeSettingsPath (a manifested relative file), requiredLabels and optional
aliases[{key,assetGuid}]. The public Addressables.kAddressablesRuntimeDataPath
PlayerPrefs setting selects the verified native RuntimeSettings before genuine
InitializeAsync. Original GUID aliases resolve existing native catalog locations
after initialization; absent targets fail. Local StreamingAssets paths resolve
to extracted content. Remote URLs are not rewritten. No empty-success providers
or private initialization flags are used.

Bounded quest-startup.log and quest-startup-state.json record provenance,
scene loads, content/catalog readiness, lifecycle and independent false gates.
State is written every five seconds/on lifecycle changes; original errors retain
24 bounded records. There is no per-frame JSON/log stream.

Focused .NET suite executes real original platform getters/initialization from
generated GH.Runtime, actual original config value persistence and reload, logger
and path APIs. Semantic fingerprints compare all untouched types after output
serialization and verify seven protected types. ZIP corruption, build mismatch,
traversal, extra entries, repeated conversion and unknown BepInEx API are negative
controls. The focused Release run passed 69 assertions. These are managed-boundary
proofs, not an actual Unity menu/device test. Without private owned-game inputs,
CI explicitly skips those actual-boundary checks and still runs portable content
and Harmony fixtures; it does not substitute reference stubs for execution.

The actual smoke invocation with existing woven ModBuild609 overrides plus the
current mod's BepInEx API succeeded at
`/home/claw/quest3-local/startup-adapter-woven-proof`. Its report verifies seven
protected types and 4,868 unrelated types, and emits a source-derived BepInEx
closure of 55 types/388 methods. That private receipt is generated evidence and
is not proprietary repository content.

RuntimeCompile compiles all new runtime code against real original Unity2021
assemblies and root's current QuestProbeInput, with zero warnings. Actual native
catalog loads, original Unity callbacks, XR pointer behavior and device rendering
still require the integrator's real Unity/IL2CPP build and headset evidence.

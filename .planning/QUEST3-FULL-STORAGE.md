# Quest full-port local storage and provider metadata

The Quest variant keeps the original `RootSaveData`, `GlobalData`,
`PartyAdventureData`, `CMapState`, `SaveOwner`, BinaryFormatter/Odin fallback and
`SerializationBinding`. This lane does not introduce a save format or cloud
services. Game progress remains local under Unity's persistent data directory.

## Integration APIs

Call `StandaloneStorage.Bind(game, compatibility.MainModule, changedTypes,
modifications)` after creating `PathsCompatibility`, before writing it. This
requires the exact owned `PlatformFileSystem.WriteFile` and original
`WriteFileAsync` Task.Run worker shape. Only their two `File.WriteAllBytes` call
operands change. Original corruption checks, queues, callbacks and IO exceptions
remain authoritative. The helper writes a unique same-directory temporary file,
flushes it, then uses `File.Replace`/`File.Move`. Replacements retain the previous
bytes as `<native-file>.ghvr-save-backup`. Android filesystem behavior remains a
hardware gate; failure does not silently fall back to truncating the live file.

Call `QuestGameSaveStorage.Initialize()` after `Paths.Initialize`, before
original `SaveData` startup. It performs one bounded backup/transfer recovery
pass, installs `QuestGameSaveLifecycle`, and at Debug executes the actual original
root/owner serializers with an isolated in-memory dummy fixture. Healthy existing
native files are never deserialized or rewritten by recovery. There is no periodic
save census or full-save verification during ordinary startup.

`QuestGameSaveLifecycle` requests original campaign and global saves on a real
pause transition. Campaign writes require native local owner admission or the
online host; multiplayer clients do not create authoritative campaign snapshots.
The local rule is the original PC/Android `NeedToCreateSave` condition: a matching
nonzero network owner or matching local account owner. Imported foreign owners
stay unchanged, and the original load flow offers its normal local-copy action.
An original queue barrier records callback completion, not guaranteed durability.
Original unsupported map phases can skip a campaign write while calling back;
Android suspension can prevent callbacks until resumed frames. No synchronous
flush loop, forced scenario ending, queue cancellation or altered serializer is
introduced. Main-menu preferences use the original global queue.

Unity documents persistent data survival while the bundle identifier stays the
same; APK installation must keep the package and signing key and use update,
never uninstall. The files are removed by uninstall/app-data reset. See
[Unity persistentDataPath](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Application-persistentDataPath.html)
and [Unity OnApplicationPause](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/MonoBehaviour.OnApplicationPause.html).

## Complete snapshot transfer

`scripts/quest-saves.py` provides `export-pc`, `import-pc`, `export-quest`,
`import-quest` and `validate`. The Windows `.cmd` defaults to exporting Quest
saves; `.ps1` accepts the named commands and uses the existing installer-local
Python/venv/ADB provisioning. Quest operations use the existing identity-checked
wireless/USB connection and stop this game's process before copying. ADB command
logs and receipts remain on the PC.

Examples (developer workflow):

```powershell
.\scripts\quest-saves.ps1 -Command export-pc -SaveRoot 'C:\path\GloomSaves' -Output '.\pc-saves.zip'
.\scripts\quest-saves.ps1 -Command import-quest -Archive '.\pc-saves.zip' -Replace
.\scripts\quest-saves.ps1 -Command export-quest -Output '.\quest-saves.zip'
.\scripts\quest-saves.ps1 -Command import-pc -Archive '.\quest-saves.zip' -SaveRoot 'C:\path\GloomSaves' -Replace
```

Close the PC game before its transfer. Export checks a stable complete tree;
import replaces the complete `GloomSaves` root, including root/global indexes,
campaigns, checkpoints and preserved Guildmaster bytes. It never merges serialized
global indexes. Existing roots require explicit `Replace` and remain in a complete
backup. Quest imports also keep a PC backup, verify uploaded bytes/marker before
moving the live root, and persist a recovery journal. Interrupted replacement
rolls back an old complete root or validates a first import before making it live.
Release/beta save roots must match. Archive bounds, file hashes, path/link/case
checks are validated before any live save change. These checks occur during
explicit transfer, not repeatedly during game startup.

## Provider-independent baked identity and DLC ownership

Profile schema 1 preserves `provider`, `displayName`, `source`, `logoSha256` and
full native player identity. Steam keeps `steamId`; optional `providerId` must
match it. Epic uses its 32-character lowercase account ID; GOG uses its numeric
account ID. Non-Steam `steamId` is the literal `0`, while `providerId` is the actual
user ID and `accountId` is a stable nonzero uint32 derived from provider + full ID.
Product/catalog/install identifiers are never treated as user identities. No
credentials, refresh tokens, authentication claims or live provider API calls are
accepted. Explicit local metadata may provide Epic/GOG display name and account
ID; installation files alone cannot reliably supply those profile fields. The
maintainer build remains visibly `Quest Local Test (DUMMY)` / ID `0`.

Steam's existing read-only local SDK ownership capture remains available.
`--owned-dlc` and strict `--owned-dlc-json` declarations are available for every
provider when installation metadata cannot establish ownership. Canonical DLC
keys/app IDs remain Jaws of the Lion `1809490`, Solo Scenarios `1958560`, alternative
Jaws skins `2584170` with mask bits 1/2/4. Explicit non-Steam JSON uses the selected
`providerId` rather than a fake Steam account. The declarations are a small local
ownership hurdle, not DRM or proof resistant to an open-source builder change.

GOG detection validates actual local `goggame-<product-id>.info` filenames against
JSON `gameId`, known Gloomhaven base/DLC titles, and DLC `rootGameId` association.
No guessed GOG catalog IDs are hardcoded. GOG describes those mini-manifests as
installed DLC indicators for Galaxy/offline installers:
[GOG DLC discovery](https://docs.gog.com/sdk-dlc-discovery/).
Unknown/ambiguous metadata fails closed with explicit declaration available.

Epic detection associates the selected game folder with launcher `.item` and
local `.egstore/*.mancpn` installation identifiers (`--provider-metadata-dir`
can select the launcher manifest directory). Those identify the product, not the
account or purchased DLC; ambiguous entitlement requires explicit declaration.
Universally bundled rules/media cannot grant ownership: both
[Steam's DLC documentation](https://partner.steamgames.com/doc/store/application/dlc)
and [Epic's DLC guidance](https://www.epicgames.com/help/c-32735058/c-Trending_0/epic-games-launcherdlc-a12640887)
describe DLC content that can already be distributed with the base game.

## Focused evidence and remaining gates

Run `python3 scripts/quest-storage-tests.py` for provider/snapshot controls and the
real emitted writer/recovery/pause fixture. The current checkpoint passes 26
Python cases and 49 actual filesystem/Cecil/lifecycle assertions. The old direct
writer interruption control demonstrates live-file truncation; the new helper
preserves native bytes/previous backup. Delayed original-style queues, clients,
foreign owners, unsupported phases and replaced save contexts are covered.

`quest-save-original-tests.py --managed <owned GH_Data/Managed> --output <private>`
executes the actual original `RootSaveData` and `SaveOwner` BinaryFormatter plus
actual `SerializationBinding` on host CLR: 12 field/type/header assertions pass.
Its private receipt includes the owned assembly/fixture hashes. This is not proof
of full `GlobalData`/`PartyAdventureData` Unity context, Android IL2CPP generic/AOT
closure, native campaign continuation, or atomic replace support on the headset.
An optional owned-managed-directory argument to `QuestStorage.Tests` also exercises
the actual original writer ABI, writes/re-reads the changed CIL in memory, and
verifies unchanged fingerprints of all original save/owner/queue/binder types
(52 assertions total). No owned game DLL is overwritten. The full native build
and real save/resume/import/export test remain required.

# Quest Guildmaster admission boundary

The excluded Guildmaster mode previously had only a disabled native main-menu
button. Original `GHClientCallbacks.Connected` accepts a host `GameToken`, whose
`GameModeID` can select Guildmaster independently of that button. Both original
`TryLaunchMultiplayerSave` overloads then call `SaveData.LoadGuildmasterMode`.
The local `GuildmasterLoadGameService` reaches the same load entry for retained
or imported adventure saves.

Full-game standalone weaving now adds two narrow, build-time seams:

* A valid host token is compared with the original `EGameMode.Guildmaster`
  constant before native user privileges, save negotiation or file transfer.
  Only that mode invokes the original optional `OnConnectionFailed` callback
  with `InvalidSessionData`, then original `FFSNetwork.Shutdown`. The existing
  invalid/missing-token branch and every other game-mode branch remain intact.
* `SaveData.LoadGuildmasterMode` returns through a Quest notice before any
  original DLC check, global save write, owner conversion or adventure launch.
  Imported adventure bytes and metadata are retained. Original cancellation
  arguments are supplied to the notice for acknowledgement; no Campaign save
  method, host token, serializer, transport or protocol is replaced.

`QuestGameScope.NotifyGuildmasterUnavailable(bool loadMenuOnCancel, Action
onCancelLoad)` is the statically referenced full-game runtime UI boundary in
`QuestGame.Campaign`. It uses the existing English/German
`Core/Loc/QuestText.guildmasterUnavailable` explanation. It owns notice lifetime
and the original acknowledgement/cancellation flow. The weaver does not patch
the desktop mod or rely on runtime IL2CPP detours.

The runtime notice retires a pre-existing native connection-error presentation
through the original `ClearHotkeySessions` and `DisposeButtons` methods before
showing its explanation. Hiding alone retains old native buttons, while the
native raw-text notice refuses to replace a visible message. Acknowledgement
invokes the original `SaveData.OnCancelCreateLocalSave(bool, Action)` exactly
once when a local caller supplied cancellation or requested a main-menu return;
the multiplayer notice otherwise closes in place. Original assembly-wide AOT
roots preserve these private native methods. A real Unity/GH.Runtime SDK probe
checks their exact signatures without invoking game or engine callbacks.

Run `python3 scripts/quest-guildmaster-notice-tests.py` for 145 managed notice
assertions and six rejected defects. The existing scope suite additionally
checks purchase/tooltip ownership with 20 rejected defects. Full-game SDK mode
on `QuestGameScope.NativeSDK.csproj` uses `-p:CampaignScope=true` and explicit
`RuntimeSource`, `QuestTextSource`, `GameManaged` and `UnityManaged` properties;
it checks 30 original ABI assertions. These are native-call and signature
checks, not a rendering simulation.

`StandaloneGuildmasterTests.Run(projectRoot, check, requireOriginal: true)` reads
the actual owned `GH.Runtime.dll`. It checks both modified methods against their
complete original bodies after removing only the generated seam, verifies
protected transport/rules and unrelated save/client methods, and executes the
actual emitted admission instructions through a CLR fixture with counter-only
external calls. Fixtures cover all eight native modes, unknown values, nullable
callbacks, missing tokens and local cancellation arguments. An inverted actual
mode branch and five changed original-ABI controls must be rejected. The default
suite may skip this original-method proof when owned inputs are unavailable;
the required original-input invocation cannot skip it.

The focused actual-input run passes 150 assertions. This proves source/managed
branch behavior; a headset-to-PC Campaign join remains a hardware acceptance
test. It does not claim device rendering or live multiplayer success.

The integrated static Weaver suite passes 904 assertions. The affected original
network/codec suite passes 11,998 assertions and 12 codec paths, retaining native
token/save/side-channel bytes. Its former 12,000 count decreases by exactly the
two newly adapted type-body invariance checks; the two complete original method
bodies and mode behavior are instead covered by the added 150 admission checks.

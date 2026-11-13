# Quest B620: scoped menu hardware follow-up

Date: 2026-10-04. Branch: `feature/quest3-standalone`; never integrate this
candidate into `dev`. This remains a private original-startup/menu diagnostic,
not a complete playable campaign or authenticated crossplay port.

## Evidence and resulting changes

The capture `quest-capture-20261004T172714Z-87b7ff36.zip` passes CRC/path checks;
installed B619 build, APK hash and input identity match the local handoff.
The supplied `dev.gloomhavenvr.quest-20261004-192006.jpg` shows the correct left
menu and a grey right/background pane. The maintainer confirms all audio is now
correct. The retained older B609/B611 hardware logs are not evidence about B619;
the paused/exited process at collection is not evidence of a crash.

The native Intro prepares, starts and exposes decoded 1920x1080 frames and
camera-plane output to the real FlatScreen target. Its original media path is
available and audio plays. A narrow Android output adapter now composes that
native decoded texture in the captured camera's current attachments. Far video
uses original foreground depth before image effects; near video renders after
the scene. It changes no player URL, audio, render mode, timing, callbacks or
native gameplay controller, and retains the shared mod's stereo-depth routing.
Ownership follows actual FlatScreen records and target references, not names,
resolution constants or a copied menu implementation. Real Unity GPU checks
cover asymmetric pixels, six aspect modes/pixel aspect, alpha, foreground depth,
actual ownership/target routing, target swaps and disposal. Real Android GLES
compilation validates both shader stages. Headset pixels still require a test.

Guildmaster's tooltip is on its attached original `UITextTooltipTarget`, not the
option's null private tooltip field. Quest binds that exact native component and
shows the localized standalone-unavailable explanation. Native missing bindings
remain visible failures. The two nonsensical rows `MixedReality/KeyColor` and
`WorldUI/DesktopMirrorLeftEye` are filtered only through the existing generic
row-visibility seam when standalone is enabled. Defaults and all other current
and future option rows remain in the shared catalog.

The native campaign party-name field permits Android's external soft keyboard;
TMP consequently rejects the existing VR keyboard's in-place events. A reversible
Quest-only lease suppresses the external soft keyboard while the existing VR
keyboard owns the field. The original ProcessEvent/validation/value-change/focus
path remains; close restores the native setting without overriding a subsequent
native edit. This does not add Horizon/overlay services or fork the keyboard.

The recovered B619 PlayerSettings disabled incremental GC. B620 enables Unity's
incremental collector only in the generated Android project; it does not force
collections or modify desktop settings. This is an avoidable full-heap pause
risk, not proof that GC caused every reported three-second hitch. A read-only
Debug accumulator records active-scene frame totals, spikes and GC deltas without
ordinary-frame allocations; first/resume/scene-transition gaps are excluded.
At most four spike lines, eight summaries and 24 spike samples are retained.
Existing periodic state snapshots include timings and their maximum write cost.
The bounded startup log reserves separate detail/startup/error budgets, retaining
late MainMenu lifecycle and first distinct failures under the same total ceiling.
Unity collector behavior: https://docs.unity3d.com/2021.3/Documentation/Manual/performance-incremental-garbage-collection.html

## Compatibility with subsequent mod development

All shared-source adaptations use central `QuestStandalonePlatform.Enabled` or
its read-only capture capability. Desktop behavior is exercised explicitly.
The builder inventories and compiles the selected current mod source and current
authored art; it does not pin a historical plugin binary or duplicate its menus.
Only genuine original/package ABI contracts and exact recovered identities are
pinned, with visible failures when they drift. Future options stay discoverable
without a Quest allowlist. A deliberate new shader/ABI requires updating its
corresponding validation evidence, not rebasing a fork of the whole mod.

## Hardware procedure

1. Extract the Windows archive and use `Install-Quest.cmd`. The installer chooses
   embedded build stamps in merged folders and confirms installed B620/input.
   Retain application data and the existing wireless ADB connection settings.
2. Check Intro picture and sound, then the menu background/3D preview. Record
   missing pixels, orientation, foreground depth and any double image separately.
3. Hover Guildmaster and confirm the standalone explanation. Open VR options:
   Key Color and Monitor Shows Left Eye must be absent; other settings remain.
4. Open the campaign party-name field, type, backspace, close and reopen the
   existing VR keyboard. Confirm actual native text and persistence in the field.
5. Leave the active menu visible for at least 60 seconds, then repeat with MR
   enabled. Describe whether the periodic hitch remains. Do not clear app data
   between first and second launches. Use `Collect-Quest-Logs.cmd` afterward;
   supply its ZIP and relevant screenshots. Debug state now contains frameTiming.

A successful desktop GPU fixture is not headset confirmation. Existing audio
repairs are retained without additional mixer/pitch/playback tuning. Shipping
performance, full campaign content and crossplay remain unproven.

## Bounded content follow-up identified from this capture

Owned DLC startup calls still request `always_loaded_dlc_1/2`, four native JoTL
CharacterConfigUI assets and the native JoTL TMP ability-card sprite atlas absent
from the current recovered startup slice. The original keys map to authentic
containers without `_ConfigUI` suffixes; inventing aliases for unrelated base
objects or empty labels would not fix this gap. The independently recorded
`B620-dlc-startup-gap-proof.json` identifies a minimal coherent base-closure
extension of exactly two original GUI bundles (78,263,098 and 38,407,202 bytes),
not campaign/scenario bundles. The next content step must re-recover the 17+2
original bundle set, verify native identity/type/GUID/dependencies, select owned
DLC preload labels and build the resulting Android catalog. This is not silently
claimed complete in the current menu-only APK. Source ownership selection is
already local; automatic GOG/Epic discovery still requires genuine provider
metadata fixtures as recorded in the B619 follow-up.

## Verified handoff and cleanup

The signed candidate uses clean native source `4621f21596d17a8e71235c72ed0227e12502d029`
and input `4b5cb809a87c322002c3b060dc49e061be7ef43261c5406140bdcf3dd5b80011`.
Later commits update only the loading fixture and developer documentation;
selected runtime/build source remains identical to the frozen input.
APK SHA-256: `2d6c25001cb5a94cc96f1a384ed530d5001aa21ccb05a4387e69b4f801fe5469`;
bytes: 2,587,906,095. Windows ZIP SHA-256:
`4b5e620800c7aba9cfa4ee8ef48043b261848c090b4d771e12c2c3cf9f726b26`;
bytes: 2,588,017,594. Signing identity remains the existing local certificate.

Final affected evidence: 18 focused Quest suites, 14 source checks, strict Release
without warnings/errors, 286,760 unchanged direct wire/golden assertions, actual
Android package SDK/GLES validation and independent compiled-boundary audit.
The initial integration loading fixture lacked the new capture stub; the fixture
was repaired and its full lifecycle/12-control rerun passes. The final boundary
audit checks 136 CIL assertions plus 96 further conditions, all 15 methods in the
actual compiled native library, and complete current 957-file mod source closure.
This proves current integration, not perpetual compatibility or headset pixels.

Final APK/ZIP native identity, ARM64 ELF headers, signatures, all ZIP entries,
embedded manifests and original media provenance are independently checked.
Isolated archive installer dry-run chooses B620 by its embedded stamp. Android
build receipt and generated settings both enable incremental GC; hardware state
will report the actual runtime collector. No new hardware outcome is claimed.
The changed shader content produces updated startup bundle/catalog hashes, so
first update preparation is possible. Second launch must use cached content.

Ignored evidence: main `.planning/debug/quest3-validation/B620/`; compact cleanup
archives: `cache-archive-B620/`. Three clean workers and the superseded B619 Unity
project are removed after process/symlink guards, preserving Git refs, matching
B619 symbols, native backups, current B620 and all canonical inputs/captures.
Measured extra free space: 36,563,001,344 bytes for those roots and 7,764,107,264
bytes for old downloads/packaging stage (44,327,108,608 combined during cleanup).
Parallel dev checkouts are untouched. Latest handoff folder contains only APK,
Windows ZIP and `handoff.json`.

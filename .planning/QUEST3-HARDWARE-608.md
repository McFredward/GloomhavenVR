# Quest 3 hardware checkpoint — ModBuild 608

This is a signed Android ARM64 IL2CPP **hardware diagnostic**, built from the
maintainer's locally owned game. It contains a genuinely recovered animated
BanditGuard visual asset. It does not start the original campaign, the complete
VR mod, original saves or multiplayer. Do not publish the APK or recovered
proprietary assets in Git or public releases.

## Private artifact and installation

The reviewed handoff is retained in the main checkout's ignored hardware-evidence
directory:

```text
.planning/debug/quest3/GloomhavenVR-Quest-B608.apk
.planning/debug/quest3/handoff.json
```

The receipt identifies the exact APK hash, source commit/input key, diagnostic
target, visibly marked dummy profile, signing certificate and actual Android
build result. Private signing material stays in the external builder output;
neither the APK nor this handoff contains the private key/password.

The package is `dev.gloomhavenvr.quest`. Enable the headset's existing developer
mode/ADB authorization, then install from a PC with Android platform tools:

```sh
adb devices
adb install -r /path/to/GloomhavenVR-Quest-B608.apk
adb shell am start -n dev.gloomhavenvr.quest/com.unity3d.player.UnityPlayerActivity
```

It can also be opened from the headset's Unknown Sources applications. For
multiple ADB devices, supply `-s DEVICE_SERIAL` to each command. Keep the same
package and signing key for updates; `install -r` preserves app data. The builder
also has an `install` command that validates the latest APK/receipt first.

The visible test account on this host is **Quest Local Test (DUMMY)**, Steam ID
**0**, account ID **0**, as explicitly authorized by the maintainer. A normal
builder invocation on the player's PC imports the selected installed Steam
account. The baked name/ID/logo are offline presentation data; no Steam, Horizon,
cloud-save or other store service is contacted by this diagnostic.

## Hardware checks

Start standing within the configured floor boundary with both controllers awake.
The diagnostic places its table in front of the initial head pose; use B to
place it again after a boundary/orientation change.

| Input/check | Expected result |
| --- | --- |
| Head and both controllers | Stereo scene follows the headset; both capsule markers/rays follow their own controllers. These markers are diagnostic geometry, not the original mod's hands/controller artwork. |
| Original model | Textured BanditGuard with six skinned meshes animates without gameplay-event errors. Original rig/avatar/clips are retained. Materials use a clearly documented Standard approximation; this is not final shader parity. |
| Right A | Toggle native passthrough. When enabled successfully, the real room should replace the opaque camera background in both eyes. The diagnostic/UI remains visible. An unavailable extension keeps VR active and reports failure. |
| Right B | Place the diagnostic table in front of the current head direction at floor level. This is an explicit placement action, not head-following. |
| Left X | Write and reread a small diagnostic JSON marker. The storage status should succeed. This is not an original Gloomhaven save. |
| Left Y | Switch English/German labels. |
| Ray over Guildmaster/Steam Workshop | Disabled grey entry with the localized exclusion tooltip; no entry action. These demonstrate presentation only, not all future original-game ingress guards. |
| Pause/resume | Open/close the headset system menu, sleep/wake once, and return to the app. Check tracking, both eyes, the chosen passthrough state and original model animation. |
| Stop/relaunch after X | Storage status restores successfully under the same baked ID. A repeated `install -r` with the same key should also preserve the marker. |

The MR panel backing belongs only to UI. There is no room-sized MR backing or
background scenery added inside the play area. The diagnostic targets 72 Hz and
shows a sampled frame counter; this small scene cannot establish scenario
performance, memory headroom or thermal stability.

## Capture evidence

Record the startup banner/build/input key and note the headset firmware version,
which inputs were tested, whether one/both eyes failed, and whether resume changed
the behavior. A screenshot/video plus the log distinguishes an image defect from
an extension/session failure.

```sh
adb logcat -d > quest3-logcat.txt
adb pull /sdcard/Android/data/dev.gloomhavenvr.quest/files/quest-hardware.log .
adb pull /sdcard/Android/data/dev.gloomhavenvr.quest/files/quest-hardware-storage.json .
```

Unity's startup banner records the actual persistent storage path; use that path
if it differs. Android scoped-storage restrictions can affect direct `adb pull`.
Keep the logcat capture in that case rather than uninstalling the app or deleting
its data. The app log is bounded and rotates to `quest-hardware.log.previous`.
The diagnostic JSON contains the baked ID; real-account logs/receipts stay private.
Store supplied evidence under the main checkout's ignored `.planning/debug/quest3/`.

## Full-game blockers remain separate

The owned-game export retains all 13 original build scenes and resolves their
MonoScript bindings. It still has 247 placeholder shader files, 16 failed
serialized behavior layouts, unresolved original references and 3,251 deferred
bundles. Original Addressables keys need an exact Android catalog mapping.
These are measured recovery gaps, not a conclusion that the game cannot be ported.

The static converter integrates the current mod's source-derived Harmony hooks
without runtime detours. Original gameplay/network protected types remain intact.
Full-game startup still needs the standalone BepInEx/config/content-root adapter,
original platform boundary and verified reflection/generic IL2CPP closure. The
original Photon/EOS crossplay route has not connected on Android. Shader fidelity,
original save round trips and all excluded-mode ingress guards remain acceptance
gates. See [implementation status](QUEST3-IMPLEMENTATION.md),
[recovery evidence](QUEST3-RECOVERY-EVIDENCE.md) and
[AOT evidence](QUEST3-AOT-EVIDENCE.md).

No Quest device was connected to the build host. APK compilation, signing,
ARM64 payload checks and native composition/lifecycle tests are automated evidence;
all headset image, controller and passthrough outcomes above remain unverified
until the maintainer runs this checkpoint.

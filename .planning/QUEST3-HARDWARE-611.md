# Quest 3 expanded hardware checkpoint — ModBuild 611

Quest development remains on `feature/quest3-standalone`. Frame610 belongs to the
parallel `dev` work. This checkpoint is still a private diagnostic built from the
locally owned game, not a playable campaign, original-save or multiplayer port.

## First hardware evidence and source conclusions

The four maintainer photographs are retained in the main checkout's ignored
`.planning/debug/quest3_probleme/` directory. They show native passthrough, both
controller markers, the recovered animated figure and a successful diagnostic
storage read. The maintainer reports misaligned pointing rays, an orange/red
figure and no joystick navigation. The pictures do not contain a build banner;
their appearance is consistent with the provided609 handoff, but exact installed
build identity cannot be established from these images alone. There is no other
player's standalone capture to compare yet.

Source inspection establishes two defects. Rays were authored from the grip pose
rather than the distinct OpenXR aim pose. The material conversion retained an
orange `_Color` value that the original Amp shader did not expose; switching to
Standard activated that dormant albedo multiplier. The actual albedo and imported
normal textures exist at 2048x2048. No joystick navigation existed in609.

The new probe reads aim and grip independently, rejects incomplete/invalid pose
states, retains grip markers and uses aim for both visual rays and collider input.
Navigation changes only the tracking origin: horizontal head-relative movement
and 30-degree head-pivot snap turns, with deadzones, bounded frame delta and neutral
controls required after tracking loss or app resume. Explicit flat ambient light
also makes the intended diagnostic lighting independent of an absent skybox.

Recovered material conversion now captures saved original texture bindings before
mutation, preserves texture/UV/normal properties and maps the original neutral
tint to white. It validates importer/Android formats and refuses unsupported
modifiers. This remains a Standard approximation; packed MRAO, character lighting,
outline/dissolve and original shader appearance are not reconstructed.

## Private installation and evidence

The new private handoff contains `GloomhavenVR-Quest-B611.apk` and `handoff.json`
under the main checkout's ignored `.planning/debug/quest3/`. Its signed ARM64
IL2CPP artifact retains the previous signing certificate and app package
`dev.gloomhavenvr.quest`; updates use `install -r` to retain the diagnostic marker.
The visibly authorized test profile is `Quest Local Test (DUMMY)`, Steam ID0.
The panel now displays actual ModBuild and a shortened builder input key.

Extract the complete Windows test package, then double-click `Install-Quest.cmd`.
The existing local Python/venv and automatic ADB workflow applies. After the test,
keep the headset awake and double-click `Collect-Quest-Logs.cmd`. It collects over
verified remembered Wi-Fi, or reads an authorized USB Quest directly. The ZIP is
written to `.planning/debug/quest3/captures/` on the PC. Copy that ZIP and relevant
screenshots into the main checkout's ignored hardware-evidence directory.
See [installer and collection details](QUEST3-WIRELESS-INSTALL.md).

The collector works without an APK and does not install, stop, relaunch, clear
logs or change headset settings. Partial captures are retained with explicit
collection gaps. Allowlisted app files, recent main/crash logcat, firmware/package
queries and optional installed-APK hashes distinguish current package evidence
from historical log banners and PC installation receipts. No automatic upload
occurs. Captures can contain profile IDs, headset identifiers and unrelated recent
Android log lines; keep them in the project's existing private evidence location.

## One hardware test battery

Use the trigger of either controller to activate a button with its ray. Switch
pages with **Next check page**. The separate panel remains fixed beside the main
panel; B explicitly places both panels and the table together in front of you.

| Check | Action and observation |
| --- | --- |
| Build/profile | Photograph the visible611 banner/input prefix. Confirm the dummy profile and two controller markers. |
| Aim and UI | Point each controller at a page button. Compare the ray with its physical pointing direction; trigger should activate once per press. Hover the grey Guildmaster/Workshop entries for the exclusion tooltip. |
| Navigation | Left stick moves horizontally in your viewing direction; right stick turns 30 degrees once until returned to neutral. Move closer to the figure, turn while standing off-centre and check that turning does not move the head around a pivot. |
| Materials/atlas | On the material page, inspect the displayed atlas and switch through all material slots. Compare **Lit approximation** with **Albedo only**. Report missing texture, orange tint, dark surfaces, transparency, missing geometry and differences between eyes separately. |
| Enlarged inspection | Change model size to 3x; inspect from several angles without physically bending down. Return to tabletop size. Enlargement is diagnostic and reversible. |
| Animation | Pause/resume animation. Confirm a frozen pose resumes and both eyes retain the same pose/appearance. Verify texture alignment on moving limbs. |
| Stereo/colour/UI | On the animation page compare the UI colour reference with each eye in VR and MR. Report text readability, missing colours or one-eye artifacts. |
| Native MR | A toggles passthrough. Repeat VR→MR→VR while checking the figure, atlas, UI, both controllers and pointing. UI backings remain on UI only. |
| Lifecycle/tracking | On the timing page open/close the system menu, sleep/wake once and briefly occlude a controller. Compare head/grip/aim tracking and origin mode after recovery; held sticks must not cause a resume jump. |
| Timing | Stay still for about 10 seconds per rendering mode, then navigate and turn. Capture the timing page. Its bounded window reports actual frame interval statistics and XR refresh when available; GPU timing remains explicitly unavailable. |
| Storage | X writes/rereads the diagnostic marker. Close and reopen the app, then verify the marker survived; repeat after a same-key update if practical. This does not test original campaign saves. |
| Capture | Use **Save snapshot** after a problematic state, then run the PC collector immediately after the session. Include short notes describing which page/mode/action caused it. |

The app periodically overwrites `quest-hardware-state.json`, recording actual
build/input identity, material bindings, render/animation/scale choices, bounded
frame statistics, head/grip/aim poses and validity, tracking loss counts, origin
and recent lifecycle events. Pause/action snapshots supplement the periodic
capture. The existing app log rotates at approximately 1 MB. These measurements
can diagnose this small probe; they do not establish full-game GPU headroom,
thermal stability, complete content recovery or platform/network readiness.

## Validation boundary

Material behavior is checked in the real original editor against the owned
recovered figure, including missing bindings, tint/UV/normal loss, unsupported
modifiers and Android format failure controls. Independent real-Unity input and
diagnostic suites exercise the production sources with injected defect controls.
Windows launchers are exercised with Legacy argument handling and controlled ADB
responses. Android build/signature checks and the complete repository gate are
recorded separately in private build evidence. No Quest is attached to this host;
new aim, material, stereo, navigation and lifecycle outcomes require this hardware
run. Full-game blockers remain in [the implementation status](QUEST3-IMPLEMENTATION.md).

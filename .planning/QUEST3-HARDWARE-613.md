# Quest original startup with the real VR mod — ModBuild 613

This private diagnostic activates the existing mod during the original
Bootstrap -> Intro -> Gloomhaven_unified -> MainMenu startup. The maintainer
requested reuse of the real rig, hands/controllers, input, menu presentation and
VR keyboard on 2026-10-03. Quest work stays on `feature/quest3-standalone`.
This checkpoint does not claim a playable campaign or confirmed headset output.

## What B612 established

The capture `quest-capture-20261003T180019Z-a1cad354.zip` identifies the installed
B612 APK by SHA-256 `567722d511049f183e35236427d4128ba6ee056398df7c4543b982efc8bf3797`.
The screenshot shows the original loading-error window after the intro. The
process reached Gloomhaven_unified but never reported MainMenu. Owned-content
verification completed for 465 files, and real Addressables label loads completed
for 281/114/15 assets. Historical B609/B611 probe files in the capture are separate
evidence, not the installed startup run.

An actual missing InputSystem getter is recorded before the load failure:
`Pointer.get_delta` was compiled to return Vector2Control, while the imported
package exposes the covariant DeltaControl getter. Repetition exhausted the
old error counter. Recent logcat later reports unresolved original rule
references, but the first rule-load exception was outside its retained window.
The first Android rule failure therefore remains unknown. The timeout while
Unity paused is insufficient evidence for an Android native crash or OOM.

Original rule libraries parse the same extracted content successfully in the
exact Unity editor: Global 946 entries, Shared 437, Guildmaster 939, Campaign 638.
The B612 IL2CPP output retained all 2,391 types in the three protected rule
libraries, including their methods/fields/properties. Separately, the original
lazy rule root still pointed inside the APK; B613 sets its existing public path
before the original initialization. This fixes a proven Android file boundary
without rewriting rules or treating the unknown first failure as solved.

## Integrated boundaries

- The builder compiles Android IL2CPP Development scripts with the final backend,
  API and input settings, then audits game/mod plugin calls against the eight
  actual player package assemblies. Editor assemblies contain desktop-only types
  and cannot substitute for this gate. The two audited covariant input getters
  are rebound. One exact InControl device-style cast to a desktop-only Switch HID
  type becomes an always-null cast on Android; Xbox/DualShock/Unknown results and
  surrounding branches stay unchanged. Unsupported differences stop before native
  player compilation;
  protected rule/network types and unrelated game types are checked unchanged.
- Gloomhaven's original UGUI `LayoutRebuilder.Enable` batching gate is restored
  in a private generated package, with its original true default and early
  MarkLayoutForRebuild guard. Original/editor files remain unchanged.
- Authored mod art is compiled as a real Android GLES3/Linear/SinglePass LZ4
  AssetBundle with type trees. A separate verified archive provides the rig/menu
  bank before the large original-content extraction. Changes to mod code do not
  invalidate the independent art cache; changes to art/recipe do.
- The plugin uses the running player-owned OpenXR session. It neither creates a
  second loader nor shuts down Unity's loader. Its existing VR input and UI run
  directly; the synthetic Quest menu/pointer/keyboard are not added.
- Existing mixed-reality behavior requests the native Quest passthrough underlay
  and transparent camera output instead of the desktop key color. Existing
  backing restrictions remain. Passthrough requests are bounded and retried
  when the native session generation changes.
- Original BepInEx configuration and logging are retained in the static runtime
  closure. Existing configuration survives updates. Fresh diagnostic installs
  enable the mod and Debug logging. Desktop self-update is not offered in this
  locally built Android variant.
- Guildmaster and Workshop remain visible, disabled, with localized hover
  explanations. Original startup may still parse Guildmaster rules while checking
  all existing saves; excluding the playable mode does not permit corrupting
  original global-data initialization.
- The startup sink receives Unity messages from worker threads, deduplicates
  before charging its error budget and reserves room for 64 distinct original
  errors with stacks. Lifecycle and real-mod messages have separate bounded
  budgets. Startup state records module completion, rig readiness and failures;
  none of those fields claims campaign readiness.

## One hardware round

Install the complete private Windows test archive using `Install-Quest.cmd`.
The update retains package, signing identity and app data. Keep the Quest awake
and the app open when running `Collect-Quest-Logs.cmd`; provide its ZIP with
photographs under the main checkout's ignored `.planning/debug/quest3_probleme/`.

| Test | Observe | Evidence needed |
| --- | --- | --- |
| Identity and early startup | B613, real-mod stage, original intro/menu | Installed APK hash, scene chronology, module/rig state |
| Original load failure | Wait once; capture promptly if the native error appears | First distinct exception with stack and rule chronology |
| Existing VR controls | Both controllers/hands and rays; select original menu/settings entries | Native callbacks, camera/ray alignment, no duplicate rig |
| Native exclusions | Hover Guildmaster and Workshop | Disabled entries and localized tooltip |
| Existing VR options | Open/close VR options; toggle mixed reality | Native passthrough, opaque VR fallback, no duplicate menu/input |
| Keyboard | If the original menu is reached, use its invite field | Existing mod keyboard, original validation and callbacks |
| Lifecycle | Open/close the system menu, sleep/wake, relaunch | No XR ownership conflict, passthrough resumes, bounded logs |
| Network boundary, conditional | If the original invite UI is available, exercise entry and validation | Existing keyboard and original offline-admission diagnostics; authenticated Android EOS is a separate gate |

This startup target still has no authenticated Android EOS runtime. An invite
can exercise native UI/validation and the offline boundary; it does not establish
successful multiplayer. The maintainer permits required EOS for the later
crossplay integration, while store-profile/cloud services remain excluded.

The diagnostic remains Debug/O0 because the recovered startup's large generated
native translation units require the proven diagnostic compiler settings. Menu
timing and the earlier small B611 scene do not establish campaign performance.
Text/window aliasing and quality optimization are deferred until startup and
mod integration have actual hardware evidence.

## Validation and remaining gates

The actual ARM64 IL2CPP build succeeded with Unity 2021.3.5f1 and the five original
startup scenes. Its 1,153,868,013-byte APK has SHA-256
`66733e82c77b1ccfd513a6097845b315c03e83713d47baf9bf241af635dbe226`.
Frozen runtime/tool source is clean `6a62daf210d0d64d6822cd2ff7904975a516654b`,
input `99fd0fece7990200066a06a131f729098696bfb347bc68857f5f756e76986393`.
Package `dev.gloomhavenvr.quest` and certificate SHA-256
`1412542b0b4cac2f1bc4941cbb4b01a5556eb8da2c34709086ab9375fd33c012`
retain update continuity. Android tools verified signature and manifest;
the builder verified ZIP integrity and AArch64 ELF headers.

The actual player-package audit checked 535 type and 1,441 member references
across 48 plugins with no unresolved issues. Four getter calls and the one exact
desktop-only HID cast were adapted; 1,897 protected and 19,517 other types remain
unchanged. The new Android UnityLinker output independently retains all 2,391
original rule types and their fields, methods and properties. These checks do
not prove rule execution on the headset.

The private Windows archive's CRC/content inventory and installer selection were
verified outside the checkout. Its 15 installer/capture dependencies match the
current source bytes; the dry run selects the B613 APK with its expected hash.
Final focused validation passes all seven Quest suites: builder (70 tests),
weaver (150 assertions), native passthrough (106 assertions), threaded startup
logging (38 assertions plus eight defect controls), platform/core lifecycle
(3,053 assertions plus eleven defect controls), bundle recipe (33 assertions)
and real content extraction (30 assertions). All 14 source checks, the strict
Release build with zero warnings/errors, documentation links and the direct
286,760 wire/golden assertions pass. Automated tests establish
source contracts and tool output; the next headset run establishes actual
initialization, pixels, controls and the first Android rule failure.

Validation is scoped to the changed Quest boundaries following the maintainer's
2026-10-03 question about repeatedly running unrelated `dev` tests. The broad
local run was intentionally stopped after 79 passing suites; it is not a complete
115-suite verdict. Its already-passing card bindings, desktop rendering, MR,
figure-bank and VR-options checks remain evidence for the affected existing
behavior. Final focused Quest suites, source checks, strict build, compiled scope,
actual Android outputs and Windows archive selection form this handoff's gate.
For a scoped repeat, use `scripts/run-test-suites.py --group local --suite ID`
with the seven Quest IDs above. `scripts/wire-tests.sh` is a wrapper for the
entire local suite; this handoff builds and executes `GloomhavenVR.WireTests`
directly for the protocol vectors. The independent compiled review accepts
exactly two new Quest types, nine changed integration types and seven
ModBuild-only types against the preserved B612 snapshot, with no removed types.

Campaign scenario generation, original save round trips, full shader parity,
native network admission/content loading and sustained memory/performance remain
independent gates. Workshop, playable Guildmaster and store/cloud services stay
outside scope. EOS is permitted only if the original crossplay path requires it.

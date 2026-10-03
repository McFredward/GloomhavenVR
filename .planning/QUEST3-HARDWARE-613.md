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

- The builder audits all staged game/mod plugin calls against the eight actual
  imported Unity package assemblies. Only the two audited covariant input
  getters are rebound. Unsupported differences stop before player compilation;
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
| Network, conditional | Only after a working menu, enter a compatible base-game PC invite with crossplay enabled | Original connection/admission chronology; campaign loading is a separate gate |

The diagnostic remains Debug/O0 because the recovered startup's large generated
native translation units require the proven diagnostic compiler settings. Menu
timing and the earlier small B611 scene do not establish campaign performance.
Text/window aliasing and quality optimization are deferred until startup and
mod integration have actual hardware evidence.

## Validation and remaining gates

Actual signed APK, frozen source/input identity, private archive checks and final
local-gate results are recorded with the handoff. Automated tests establish
source contracts and tool output; the next headset run establishes actual
initialization, pixels, controls and the first Android rule failure.

Campaign scenario generation, original save round trips, full shader parity,
native network admission/content loading and sustained memory/performance remain
independent gates. Workshop, playable Guildmaster and store/cloud services stay
outside scope. EOS is permitted only if the original crossplay path requires it.

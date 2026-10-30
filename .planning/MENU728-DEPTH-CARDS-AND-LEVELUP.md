# Build728: menu depth, original level-up presentation and raised cards

## Hardware report and exact inputs

The maintainer accepts727 character size. Oblique views still allow later flat UI
to cover character surfaces that should project in front. Level-up Continue looks
custom, middle card text is clipped between the side sections, and selection
confirmation still floats separately. The new feature request raises complete
original ability/item cards on flat windows, including hover previews.

Both supplied local logs identify727/7b4054063, built2026-10-27 13:50:39 UTC, on
SteamVR/OpenXR. Remote717 logs are stale. New screenshots were not supplied.
Exact input bytes/hashes and bounded relevant rows are retained under ignored
debug/menu728/hardware-inputs. The727 source, real Debug/Release DLLs and original
rendering resource remain pinned separately; no game references are modified.

## Source findings and ownership

The original native character colour render is drawn through a UI RawImage. That
preserves eye parallax but loses the scene's depth relative to later UI passes;
the original preview aperture also clips foreground protrusions. Depth must
survive composition, with real printed-card silhouettes rather than rectangular
helper occlusion. The native scene must not render twice just to collect depth.

UILevelUpWindow has a transparent ClickTrackerExtended, not a native Continue
button. The old view constructs its own Image/TMP button. Adopt the complete
original reward Continue presentation while retaining the level-up tracker's
permission and callback as authority. ExtendedButton's additional input-manager
gate must not become a new blocker through the borrowed visual widget.

ConfirmationOnOwner explicitly rejects a different scene. The fresh log places
ConfirmationBox_pc in DontDestroyOnLoad, while its level-up owner is scene-local.
A persistent original needs a visually co-located, chrome-free capture member;
never move that native controller into a shorter scene lifetime. Complete middle
text ink and deterministic frame width must preserve the original text scale,
both side panes and the accepted window pose/shared geometry.

Native FullAbilityCard, MiniAbilityCard, ItemCardUI, MonsterRoundCardUI,
MonsterBaseUI, UIPerkAttackModifier and UIScenarioAttackModifier roots own complete
menu-card presentation. Lift their original subtrees during capture and restore
the exact pose afterwards. Native header/action/item/monster/modifier graphics
supply precise silhouettes. Nested cards share the lifted parent plane while
retaining their own exact printed silhouettes.
Scope enrollment to actual menu windows/native screen-space UI, excluding physical
hand/board/NPC cards. Real pooled births, reparenting and late layouts must reach
the first visible capture without recurring global or hierarchy sweeps.

Three disjoint worktrees begin at currentdev7b4054063: native level-up presentation,
character depth transport/composition, and card enrollment/lifting. Root owns the
build728 stamp, shared header integration, inventories/manifests, final review,
complete local gate and dev push. Workers retain focused source/native/GPU proofs
and failed hypotheses, and clean only completed private projects after process
reaping. Final integrated results will follow the frozen runtime.

The727 character switch stays default-on on every system. Existing native actions,
story/movie media and shared absolute world geometry remain authoritative. The
maintainer's Windows installation script builds the archive; no local ZIP is
requested. Source/GPU evidence remains separate from the eventual headset review.

## Integrated implementation checkpoints

The original colour RenderTexture supplies its own depth subelement through
Unity2021.3.5 Material.SetTexture. No second native model render, separate depth
attachment or runtime readback is needed. A source-specific UI material reconstructs
the same original scene surface into the common2.4-unit character cabinet. Behind
fragments retain original Stencil/RectMask2D clipping; real foreground fragments
can extend outside that aperture. Conservative sampling bounds use cached renderer
identities and current native bounds, without changing model pose, scale or clock.
The native scene target, partial viewport and image-effect path remain original.

Early complete-card transformation belongs at the existing Canvas pre-render
batching boundary. A late empty marker Graphic can miss a freshly spawned card's
first visible batch. The production marker is therefore a MonoBehaviour on the
original card root, using its real visible printed Graphic for registration.
A proposed global post-prime Canvas rebuild was independently tested with and
without it and removed; it is not the first-draw fix. Read-only disabled native
controllers remain eligible when their original card graphics are visible.

UILevelUpWindow has no visible authored Continue to reuse. The entire original
UICampaignRewardWindow Continue widget is cloned under inactive construction;
cloned gameplay/input scripts and serialized reward callbacks are retired before
activation. Native graphics, sprite transitions, font, layout and localization
remain. A narrow pointer relay invokes the existing level-up TryConfirm authority,
without importing ExtendedButton's additional reward/ESC input-manager gate.
Preview fitting includes native TMP ink and late pooled text births. A persistent
confirmation gains a chrome-free visual member following its accepted owner's
matrix; its native object retains DontDestroyOnLoad lifetime and callbacks.

A read-only audit of all original UI cameras disproves a blanket screen-UI Always
depth-test assumption: the authored UI/TMP/Dissolve materials use the dynamic
unity_GUIZTestMode, and an actual ScreenSpaceCamera GPU control respects existing
foreground depth. Original UI cameras share orthographic size100, near=-100,
far1000 and planeDistance100; serialized Canvas zero TRS is only a placeholder.
Depth comparisons and outer transparent carriers must use the live native basis.
The final multi-camera, rear and whole-window edge contracts pass their real
Unity GPU controls, separately from eventual headset acceptance.

## Final composition contracts under integration

The display photograph now carries actual foreground depth into the head render,
including a separate foreground-only depth pass. Native flat/translucent UI stays
colour-only. A two-panel GPU control distinguishes UI behind the protruding model
from UI genuinely in front. Both model-source and final-carrier clipping expand
for real foreground surfaces; flat UI outside its original frame stays hidden.
The existing SS left/right targets grow proportionally through the real allocation
policy, retaining native pixel density within unchanged panel/session budgets.
Native frame, layout, hit rectangle and synchronized host geometry do not grow.

Original UI cameras use temporary real orthographic clipping planes for a common
plane-relative metric. A failed custom-projection hypothesis visibly rescaled the
native ScreenSpaceCamera Canvas during Camera.Render and was discarded. Real
near/far values preserve its native XY projection and live basis, including
partial viewports and a second camera with different authored parameters. The
normal path does not change native Canvas mode. Native image effects execute;
depth attachment and foreground-over-UI pixels are checked after OnRenderImage.

Capture-bound calculation first filters the optional foreground-bounds interface.
Ordinary card sources never enter per-eye bounds lookups; a64-card control proves
zero card getter accesses for that additional calculation. This prevents an
otherwise quadratic query introduced by the wider menu-card registry. Original
complete-card depth uses the actual X/Z scale ratio so native XY-only subview
fitting preserves the same small physical proportion.

## Native material and discovery qualification

All12 real Character3D RawImages use Character 3D Mask / UI/Inverted soft alpha
mask (compatible with canvas group). The wrapper retains the actual native DXBC
semantics: separate MainTex/Mask transforms, inverse mask alpha, vertex colour
without material_Color, original stencil, and source-alpha blending on the alpha
channel. Destination alpha remains A² + Dalpha*(1-A), including previously drawn
partial/opaque UI. The GPU reference is a semantic translation of the pinned
native programs; it does not claim native Windows bytecode executed on Linux.
Only real foreground fragments bypass the physical planar aperture.

A400-image, two-root CanvasScaler fixture performs48 real captures over24 frames.
Warm root/ancestor/behaviour censuses stay unchanged; real parent changes and an
image born during onPreRender remain discoverable. No direct or deferred Canvas
hierarchy callback is observed for these actual root-mode leases. The proposed
observer/known-Graphic suppression was therefore removed. The early proposed
post-prime global Canvas rebuild was also removed after independent comparison.

A read-only audit distinguishes seven printed card families from ordinary UI.
The six BattleGoalPickerSlot instances are512x227.91 quest/reward columns with a
transparent, sprite-free hit target, separate paper title, scrollable rewards and
vertical quest frame. They are not a native printed-card face; raising the hidden
button would be incorrect. Obsolete AttackModifier/AttackModifierCardGUI consumers
are likewise not substitutes for the actual perk/scenario modifier card families.
The actual native card FX shader's compiled parameters do not use _PosAndBounds;
no unnecessary material-vector mutation is introduced.

## Final delivery and evidence

Runtime source is frozen at56231b6b3. Strict real Debug and Release builds pass with
zero warnings/errors, as does the private metadata build against RefAsm. All16
source checks pass. Source census preserves all670 configuration keys and248
literal Harmony entries; the sole added log token is LEVEL-UP NATIVE CONTINUE
(4840 total), with no removals. Patch inventory remains218 classes/318 methods,
registered once. Player documentation passes EN/DE structure/link checks.

Compiled comparison uses the exact727 Release baseline and real game references,
with no adjacent XML input. Only the12 intended existing menu types change;
nine new helper/API types replace the old custom ContinueButton class. Nine
other existing types contain only the literal727-to728 build substitution.
Only exact declared build timestamps/commit identities are normalized.

The33237-byte rendering bundle is built by exact Unity2021.3.5f1 for Windows64,
with D3D11/OpenGLCore/Vulkan, type trees and runtime load/support verification.
All provenance hashes match. Movies and prepared story assets remain unchanged.
The Windows installation script builds the archive; delivery is origin/dev source.

Root debug/menu728 retains exact hardware inputs, baseline source/DLLs, executed
assemblies, native audits, source pins, captures, failed hypotheses and honest
worker scopes. Final worker evidence is byte-verified before retiring all three
clean worktrees. Owned processes stop/reap before cleanup; protected pins and
borrowed inputs survive. Complete invocation, bounded fixture repairs and golden-vector results are
recorded below without claiming an all-suite pass from a focused rerun.

No Build728 headset, D3D11/Vulkan execution, real Beautify appearance or paired
multiplayer hardware result is claimed. Native depthless post-effect pixels retain
colour in the original aperture, without invented geometry depth. Actual native
widget/font/assets and Unity source kernels run; synthetic bodies, session/XR
observations and named surrounding conversion-service ports remain explicit.

## Complete integration qualification

The complete local invocation records all252 suites:250 pass, with two fixture
failures. Desktop's old negative control confused conservative crop width with
actual sampling tier; the repaired assertion checks both the real SamplingEdge
and allocated growth. Performance's separate mutation assemblies retained the
first Unity bundle identity; the completed case now unloads it after retiring all
consumers/graphics. Both repairs are test-only. Their complete affected suites
pass2/2 in the final scheduler rerun. All other passing evidence is reused. This
is complete aggregate252-suite coverage, not a new passing252-suite invocation.
All306431 golden-vector assertions pass separately after that invocation's early
nonzero exit, using a pinned executed binary. Runtime source remains byte-identical
to the strict-build/source-gate/compiled-review tree throughout both repairs.

Root evidence: full-gate/suites/results.json; fixture-final/results.json;
goldens/run-yioz9q2c; strict/run-i_fr6ywe; metadata/run-r4mp4wgu;
source-final/results.json; source-surface-final.json; compiled/scope-review.json;
verification-scope.json and integration-fixture-repairs.json under debug/menu728.
The complete invocation's2663 source/test/script inputs stayed unchanged until
it finished. Only the two identified fixture files changed afterwards; the exact
product source/resource identity is verified independently.

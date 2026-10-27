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
Those final multi-camera, rear and whole-window edge contracts are being qualified
separately from the already passing front-side source composition and card cases.

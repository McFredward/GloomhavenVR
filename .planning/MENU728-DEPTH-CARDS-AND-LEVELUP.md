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

Native FullAbilityCard, MiniAbilityCard and ItemCardUI roots own complete card
presentation. Lift their original subtrees during capture and restore the exact
pose afterwards. Native header/action/item graphics supply precise silhouettes.
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

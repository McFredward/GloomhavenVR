# NPC645: continuous native ring and stable enhancement rows

## Hardware evidence and cause

Both supplied multiplayer logs identify Build643, on RTX4090/5070Ti. Frozen
logs, the maintainer's `pristerin_menu.mp4`, hashes and chronological frame
review live under the main checkout's private `.planning/debug/npc645/`.
Despite its filename, the video shows the enchantress enhancement list. Native
hover moves its content 14 pixels horizontally; the video additionally shows
individual rows moving vertically through their neighbours. No current new
screenshots were supplied. Neither sparse video frames nor the existing
admission-clock log ages establish per-frame network latency.

The row defect is reproduced through native original capture, artwork refresh,
the actual codecs, compact numeric reception and remote playback. After a newer
caption/artwork frame, the unchanged root header is legitimately older and
omitted from a child-only hover update. `TownServiceBinding.Apply` nevertheless
forced the original top-anchored root to the centre. Without a later header
write to correct it, the row moved through its neighbours. Retaining its native
anchors and preserving its independently authored pose fixes that layout seam.
Detached canvas roots still use their existing explicit pivot-pinning caller.

The ring has two animation writers: generic native-property playback and the
final offered-card frame fit. Faster scale/layout/artwork updates can disturb
the sparse ring interpolation clock; repairing only the first writer would
still leave corrections by the final writer. A final-frame proof must also use
the real immutable template bank: it strips `UIEnchantressEffect` before later
registration, so reading the live controller only at registration is too late.

## Explicit parity exceptions

The maintainer's 2026-10-08 clarification permits different intrinsic phases for
the offered enchantress ring spin, with unchanged native direction and speed.
Its plane and facing must follow the physical card. The same follow-up permits
different phases for the intrinsic up/down hover of offered merchant and
enchantress cards, with unchanged original amplitude, waveform and speed. It
does not permit different facing, content, enhancement overlay placement,
visibility, interaction state or flights. Both exceptions are recorded in
`AGENTS.md`; no separate bob rewrite is included merely because it is permitted.

Visitor-local town pre-drop guides remain the previously approved exception.
Actual offered cards, native enhancement overlays, list entries, confirmations,
NPC poses, audio and returns retain their existing shared paths. Scenario card
secrecy and board guides are untouched. Observer originals still have no native
gameplay callbacks or independent layout/animation controllers.

## Focused verification

The completed hover fixture passes 107,480 checks. Its baseline includes a real
compact kind1 header; the caption refresh then leaves the actual composed hover
pass with only kind2 child properties. Stationary root Y remains unchanged at
all intermediate samples. Native 14px hover, scroll/fit changes, four anchor and
pivot layouts, root-header presence/absence and detached roots are covered.
The exact old behavior fails with 0.09999996m of unrelated Y drift. Its actual
Unity readback overlaps Wound with Confusion, matching the hardware defect.
Fixed owner/observer readbacks differ above the defined RGB threshold in only
66 of 393,216 pixels, versus 90,778 under the old source. The existing
`motion-fast` suite passes 864 production checks and five negative controls.

Simple fit/hover cases initially passed unchanged643; they had no newer
artwork epoch followed by child-only updates. An anchored/local coordinate
rewrite was therefore rejected. One initial fixture failed during setup; this
is retained as a test failure, not evidence of the game's defect. Preserving
only position while still centring anchors also failed and was discarded.

Final ring, exact-source integration and compiled-scope receipts are recorded
when complete below. Existing native first-picture639 and643 coverage remains
inherited for unchanged admission/assets/transport areas. These scoped local
Unity checks are not a new complete-suite pass, a WAN latency guarantee or a
new headset acceptance. The next hardware run checks the actual ring and list
appearance in the supplied multiplayer workload.

#!/usr/bin/env bash
# Re-render the README's asset strips from the SHIPPED bundle assets.
#
# THIS FILE EXISTS BECAUSE THE RECIPE WAS ONCE ONLY IN A SHELL HISTORY. The strips were rebuilt at
# ModBuild 247 and the exact per-asset flags — which side each hand is photographed from, which
# ones are culled, which run the unlit shader — lived nowhere. The next asset delivery then could
# not reproduce the frame it was replacing. Every flag below is a decision with a reason; keep them
# here, not in a terminal.
#
#   --cull   match the shipped material's _Cull. Hands are _Cull: 2 (Back). Rendering them
#            double-sided draws each hand's own interior through itself AND lets you admire the
#            arcane runes THROUGH the palm, which is how a wrong view survived a review round.
#   --unlit  the masks run GloomhavenVR/HeadUnlit (albedo x tint), not BoardLit. Putting BoardLit's
#            Lambert on a pre-lit texture is double-shading.
#   --normal bind the set's normal map when it has one. All three hands do as of ModBuild 248.
#   no --scale/--focus-z: the user asked for the FULL hand, cuff included, so each asset is
#            auto-fitted to its own complete bounds rather than pinned to a shared span.
#
# Usage:  bash unity/asset-preview/build_asset_strips.sh [outdir]
set -euo pipefail

BLENDER="${BLENDER:-/home/claw/blender-4.2/blender}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
B="$ROOT/unity/GloomhavenVR.Assets/Assets/Bundle"
OUT="${1:-$ROOT/render}"
mkdir -p "$OUT"

r() { "$BLENDER" --background --python "$ROOT/unity/asset-preview/render_asset.py" -- "$@" >/dev/null; }

# ---- hands: back of the hand, the side every set carries its decoration on ----
r "$B/Hands/VRHand_L_rig.fbx"       "$B/Hands/VRHand_albedo.png"       "$OUT/hand_glove.png" \
  --normal "$B/Hands/VRHand_normal.png"       --cull --res 900 --yaw 215
r "$B/Hands/VRHandPlate_L_rig.fbx"  "$B/Hands/VRHandPlate_albedo.png"  "$OUT/hand_plate.png" \
  --normal "$B/Hands/VRHandPlate_normal.png" --mrs "$B/Hands/VRHandPlate_mrs.png" \
  --cull --res 900 --yaw 215
r "$B/Hands/VRHandArcane_L_rig.fbx" "$B/Hands/VRHandArcane_albedo.png" "$OUT/hand_arcane.png" \
  --normal "$B/Hands/VRHandArcane_normal.png" --cull --res 900 --yaw 215

# (the masks are rendered by their own line in build_readme_images.py history — see docs/img/README.md)
echo "[strips] rendered into $OUT — now: ASSET_RENDER_DIR=$OUT/ python3 unity/asset-preview/build_readme_images.py"

# ---- control boards --------------------------------------------------------------------
# THE BOARDS WERE THE LAST STRIP TO BE FIXED and they were wrong in three ways at once (user:
# "die Controlboards werden nicht richtig gerendert in der README, sie sehen dort kaputt aus"):
#   * NO NORMAL MAP. All three tray materials bind one and always did; without it the carved
#     recesses, the wood grain and the brass corner straps have no relief at all and the tray
#     reads as a painted plank.
#   * NO --cull, AND THAT IS CORRECT HERE. Every tray material carries _Cull: 0 (double-sided),
#     unlike the hands' _Cull: 2 — so this is not an oversight to be "fixed" by copying the hand
#     line. Read the .mat.
#   * PITCH 18 IS EDGE-ON. A tray lies FLAT. Shot from 18 degrees it is a sliver; the player looks
#     down at it, so 50 does what his eye does.
# --scale pins all three to ONE ortho width. They are 639-640 mm wide in the room and their DEPTH
# differs (318 / 369 / 218 mm), so auto-fit sized each to its own box and the strip said the brass
# one was half again as big as the others. It is not.
#
# NAMES: PlayTray_16vm268h is BRONZE and PlayTray_9capjqp6 is STEEL (BoardFrame.cs:68-69,
# VRCardFactory.cs:29-30). They are easy to swap and were swapped once; check the source, not the
# look of the render.
TB="$B/Table"
r "$TB/PlayTray_prepped.fbx"    "$TB/PlayTray_albedo.png"           "$OUT/board_oak.png" \
  --normal "$TB/PlayTray_normal.png"           --res 900 --yaw 35 --pitch 50 --scale 0.78
r "$TB/PlayTray_9capjqp6.fbx"   "$TB/PlayTray_9capjqp6_albedo.png"  "$OUT/board_steel.png" \
  --normal "$TB/PlayTray_9capjqp6_normal.png"  --res 900 --yaw 35 --pitch 50 --scale 0.78
r "$TB/PlayTray_16vm268h.fbx"   "$TB/PlayTray_16vm268h_albedo.png"  "$OUT/board_bronze.png" \
  --normal "$TB/PlayTray_16vm268h_normal.png"  --res 900 --yaw 35 --pitch 50 --scale 0.78

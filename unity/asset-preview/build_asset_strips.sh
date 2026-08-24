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

echo "[strips] rendered into $OUT — now: ASSET_RENDER_DIR=$OUT/ python3 unity/asset-preview/build_readme_images.py"

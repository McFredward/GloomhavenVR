using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // Build 620 hardware: the split stone frame remains solid while fx_sparks_drop,
        // distort and fx_sparks under its own CR_St_WallTorch_Fire are carried by Wall 5.
        // The arch ends at z=-9.9, but these authored torch emitters sit at z=-9.8.
        // Widening every arch rect would also protect unrelated wall-mounted flames.
        // Instead inherit protection through the actual original attachment hierarchy.
        // Whole-game original PCG inspection found 33 doorway/effect families in 17
        // databases. Primitive doorway prefabs parent torch/candle holders and frame
        // meshes directly, sometimes as siblings; CandleFlame mesh layers can also be
        // immediate prefab siblings. The closed identities below come from that audit.
        // The live frame must still pass the existing persistent-rect rule. Larger
        // entrance/exit feature parents are deliberately not attachment roots; their
        // actual nested primitive doorway still supplies the narrow inheritance.
        // No result is retained across frames: every ownership decision observes the
        // current pooled/reparented attachment. This only governs renderers; lights remain
        // untouched, and ordinary wall sconces still use the existing animated fade.
        private bool IsArchMountedEffect(Renderer renderer, string? name = null)
        {
            bool candleLayer = NativeArchName(name ?? renderer.name) == "CandleFlame";
            if (!candleLayer && !(renderer is ParticleSystemRenderer) && !(renderer is SpriteRenderer))
                return false;
            if (renderer.GetComponentInParent<ActorBehaviour>(true) != null
                || renderer.GetComponentInParent<CInteractableActor>(true) != null)
                return false;
            bool torchMount = false;
            for (Transform? node = renderer.transform; node != null; node = node.parent)
            {
                if (node.GetComponent<ProceduralWall>() != null
                    || node.GetComponent<ProceduralMapTile>() != null)
                    return false;

                MeshRenderer? mount = node.GetComponent<MeshRenderer>();
                if (mount != null)
                {
                    MeshFilter? filter = node.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null
                        && NativeArchMountMeshes.Contains(filter.sharedMesh.name))
                    {
                        torchMount = true;
                        // A retained physical holder also supplies exact attachment
                        // provenance when its authored effect projects past the rect.
                        if (IsArchProtected(WallCommitGeometryReads.Read(mount), mount.name))
                            return true;
                    }
                    // A real frame ancestor is a physical attachment, independent of
                    // how far its native child emitter projects in front of the stone.
                    if (torchMount && mount.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0
                        && IsArchProtected(WallCommitGeometryReads.Read(mount), mount.name))
                        return true;
                }

                if (!NativeArchEffectRoots.Contains(NativeArchName(node.name)))
                    continue;
                if (!torchMount && !candleLayer)
                    return false;
                // Split02 parents its torch and frame as siblings. Only the immediate
                // original frame child can supply the protection, never a nearby wall.
                for (int i = 0; i < node.childCount; i++)
                {
                    Transform child = node.GetChild(i);
                    if (IsNativeProtectedArchMesh(child))
                        return true;
                    // This original primitive nests its frame one level under the
                    // named split-door node; its neighbouring fort wall is not read.
                    if (NativeArchName(child.name) == "ST_Vermling_Door_Split")
                        for (int j = 0; j < child.childCount; j++)
                            if (IsNativeProtectedArchMesh(child.GetChild(j)))
                                return true;
                }
                return false;
            }
            return false;
        }

        private bool IsNativeProtectedArchMesh(Transform node)
        {
            MeshRenderer? frame = node.GetComponent<MeshRenderer>();
            MeshFilter? filter = node.GetComponent<MeshFilter>();
            return frame != null && filter != null && filter.sharedMesh != null
                && NativeArchFrameMeshes.Contains(filter.sharedMesh.name)
                && IsArchProtected(WallCommitGeometryReads.Read(frame), frame.name);
        }

        private static string NativeArchName(string name)
        {
            // Unity's own clone/numeric suffixes are permitted; arbitrary similarly
            // named feature containers must not acquire the frame exception.
            int end = name.Length;
            while (end > 3 && name[end - 1] == ')')
            {
                if (end >= 7 && string.Compare(name, end - 7, "(Clone)", 0, 7, StringComparison.Ordinal) == 0)
                {
                    end -= 7;
                    continue;
                }
                int open = name.LastIndexOf(" (", end - 1, StringComparison.Ordinal);
                if (open >= 0 && open + 2 < end - 1)
                {
                    bool digits = true;
                    for (int i = open + 2; i < end - 1; i++)
                        digits &= name[i] >= '0' && name[i] <= '9';
                    if (digits)
                    {
                        end = open;
                        continue;
                    }
                }
                break;
            }
            return end == name.Length ? name : name.Substring(0, end);
        }

        private static readonly HashSet<string> NativeArchMountMeshes = new(StringComparer.Ordinal)
        {
            "EN_CR_WallTorch_01", "CR_INT_Wall_Candles_01", "CR_INT_Wall_Candles_02",
        };
        private static readonly HashSet<string> NativeArchFrameMeshes = new(StringComparer.Ordinal)
        {
            "DLC_TH_Vermling_Door_Frame",
            "CR_INT_Stone_Doorway_02_FRAME_Split",
            "CR_INT_Wooden_Doorway_01",
            "CR_INT_Wooden_Doorway_01_Split",
            "SE_Doorway_Thick",
            "SE_Gothic_Door_Frame_Thick",
            "SE_Gothic_Door_Frame_Thick_Split",
            "SE_Gothic_Door_Frame_Thick_Top_Half",
            "TO_EXT_House_Door_trim",
            "TO_INT_Shack_Doorway_Frame",
            "TO_INT_Shack_Doorway_Frame_Thin",
            "TO_INT_Shack_Doorway_Split",
        };
        private static readonly HashSet<string> NativeArchEffectRoots = new(StringComparer.Ordinal)
        {
            "CR_Dungeon_Doorway_01_PR",
            "CR_Dungeon_Doorway_01_Thin_PR",
            "DLC_SB_Platform_Doorway_01_PR",
            "DLC_SB_Platform_Doorway_01_Thin_PR",
            "DLC_SB_Ship_Doorway_01_Thick_PR",
            "DLC_SB_Ship_Doorway_01_Thick_Split_PR",
            "DLC_SB_Ship_Doorway_01_Thin_PR",
            "SE_Rot_Sewer_Doorway_01_PR",
            "SE_Rot_Sewer_Doorway_01_Thin_PR",
            "ST_Vermling_DoorFrame_PR",
            "TO_EXT_Stone_Doorway_01_FRAME_Split_PR",
            "TO_EXT_Stone_Doorway_02_FRAME_Split_PR",
            "TO_EXT_Wood_Doorway_02_FRAME_PR",
            "TO_INT_Cathedral_Doorway_01_PR",
            "TO_INT_Cathedral_Doorway_01_Split_PR",
            "TO_INT_Cathedral_Doorway_01_Thin_PR",
            "TO_INT_Stone_Doorway_01_FRAME_Split_PR",
            "TO_INT_Stone_Doorway_02_FRAME_Split_PR",
            "TO_INT_Wood_Doorway_01_FRAME_PR",
            "TO_INT_Wood_Doorway_01_FRAME_Split_PR",
            "TO_INT_Wood_Doorway_02_FRAME_PR",
            "TO_INT_Wood_Doorway_02_FRAME_Split_PR",
            "TO_INT_Wood_Shack_Doorway_01_PR",
            "TO_INT_Wood_Shack_Doorway_01_Split_PR",
            "TO_INT_Wood_Shack_Doorway_01_Thin_PR",
            "TO_SB_GothicTunnels_Doorway_01_PR",
            "TO_SB_GothicTunnels_Doorway_01_Split_PR",
            "TO_SB_GothicTunnels_Doorway_01_Thin_PR",
            "TO_Sewer_Doorway_01_PR",
        };
    }
}

using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private Material? _questOpaqueUnderlay;

    /// <summary>
    /// Capture071329 proves that B675's full eye viewport fixes tracking distortion,
    /// while both captured menu layers and their selected textures contain pixels
    /// but the headset menu is black. Keep that confirmed viewport fix. MultiPass
    /// already selects the ordinary left/right textures in OnPreRenderCamera; it
    /// needs no instanced/multiview vertex program on these world-space meshes.
    /// Use the existing owned ordinary-mesh shader for this precise mode. This
    /// avoids an unnecessary stereo shader boundary; the capture does not prove
    /// which GPU vertex state caused the black picture. The owned bank can still
    /// contain Unity-generated keyword variants; this does not force global
    /// keywords or assert that its bank is mono-only. Desktop and shared-eye
    /// modes retain their previous shaders, and no shader bank is rebuilt.
    /// </summary>
    private static Material CreateScreenMaterial(Shader? shader, Texture capture, bool glass)
    {
        bool ordinaryQuest = QuestStandalonePlatform.Enabled && !QuestStandalonePlatform.SharedStereoEyeRouting;
        Shader? owned = ordinaryQuest ? Cards.PlayTray.OverlayShader() : null;
        if (owned == null || !owned.isSupported)
            return new Material(shader) { mainTexture = capture };

        var material = new Material(owned) { mainTexture = capture, color = Color.white, enableInstancing = false };
        material.SetFloat("_VertexColor", 0f);
        material.SetFloat("_UseAlphaMask", 0f);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)(glass ? CompareFunction.LessEqual : CompareFunction.Always));
        // Both native layers already contain composed RGB. Do not multiply by
        // alpha again. The opaque screen uses this same blend over the black
        // alpha-one underlay below: RGB = source and A = a + 1*(1-a) = 1, including
        // zero/partial source alpha. Glass has no underlay and keeps native alpha.
        material.SetInt("_SrcBlend", (int)BlendMode.One);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.renderQueue = glass ? 3000 : 2000;
        return material;
    }

    private void SetScreenMaterial(Renderer renderer, Material material, bool glass)
    {
        if (glass || !QuestStandalonePlatform.Enabled || QuestStandalonePlatform.SharedStereoEyeRouting
            || material.shader.name != "GloomhavenVR/Overlay")
        {
            renderer.sharedMaterials = new[] { material };
            return;
        }
        if (_questOpaqueUnderlay == null)
        {
            _questOpaqueUnderlay = new Material(material)
            {
                name = "GloomhavenVR.QuestScreenOpaqueAlpha",
                mainTexture = Texture2D.whiteTexture,
                color = Color.black,
            };
            _questOpaqueUnderlay.SetInt("_DstBlend", (int)BlendMode.Zero);
        }
        SyncScreenUnderlayQueue(material.renderQueue);
        // Unity repeats the last submesh for extra renderer materials. Reuse the
        // exact existing quad without a second capture, mesh, collider or transform.
        // Keep the consuming material first: the existing stereo compositor reads
        // sharedMaterial. Its lower queue draws the black pass before that first
        // material regardless of the array order.
        renderer.sharedMaterials = new[] { material, _questOpaqueUnderlay };
    }

    private void SyncScreenUnderlayQueue(int queue)
    {
        if (_questOpaqueUnderlay != null && _questOpaqueUnderlay.renderQueue != queue - 1)
            _questOpaqueUnderlay.renderQueue = queue - 1;
    }
}

using System.IO;
using System.Runtime.CompilerServices;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

/// <summary>The existing physical item-card rim and reverse, shared by counter cards and
/// their inert multiplayer template. The face remains the game's original ItemCardUI.</summary>
internal static class TownServiceCardBody
{
    private sealed class Owned
    {
        internal MeshRenderer Renderer = null!;
        internal Material[] Sources = null!, Copies = null!;
        internal Texture?[] Textures = new Texture?[2];
        internal float Visibility = -1f;
    }
    private static readonly ConditionalWeakTable<GameObject, Owned> Materials = new();

    /// <summary>Unit width/height; scale x/y to the original face's measured physical size.
    /// Z remains one so the existing card mesh retains its real millimetre thickness.</summary>
    internal static GameObject Create(Transform parent)
    {
        var body = new GameObject("PhysicalCardBody");
        body.transform.SetParent(parent, false);
        CardMesh.AttachBody(body.AddComponent<MeshFilter>(), CardBodyKind.Item, 1f, 1f);
        var state = new Owned { Renderer = body.AddComponent<MeshRenderer>(), Sources = new[]
        {
            CardMesh.CreateEdgeMaterial(CardBodyKind.Item),
            CardMesh.CreateBackMaterial(CardBodyKind.Item)
        } };
        state.Copies = new[] { new Material(state.Sources[0]), new Material(state.Sources[1]) };
        state.Renderer.sharedMaterials = state.Copies;
        Materials.Add(body, state);
        SetVisibility(body, 1f);
        VRLayers.Apply(body);
        return body;
    }

    /// <summary>CanvasGroup does not affect MeshRenderer. Fade the owned body materials on the
    /// exact same opening clock as its original face; these values are captured by the existing
    /// mesh-material wire binding. Never mutate the item fan's shared materials or use an MPB.</summary>
    internal static void SetVisibility(GameObject body, float value)
    {
        if (!Materials.TryGetValue(body, out Owned state)) return;
        value = Mathf.Clamp01(value);
        bool changed = state.Visibility != value;
        for (int i = 0; i < state.Sources.Length; i++) changed |= state.Textures[i] != state.Sources[i].mainTexture;
        if (!changed) return;
        state.Visibility = value;
        state.Renderer.enabled = value > 0f;
        for (int i = 0; i < state.Sources.Length; i++)
        {
            Material source = state.Sources[i], copy = state.Copies[i];
            // Original silhouette completion may replace the shared texture after the rack
            // opens. Keep its cutout/queue at full opacity and only override the fade interval.
            copy.CopyPropertiesFromMaterial(source); state.Textures[i] = source.mainTexture;
            if (value >= 1f) continue;
            Color color = source.color; color.a *= value; copy.color = color;
            if (copy.HasProperty("_SrcBlend") && copy.HasProperty("_DstBlend"))
            {
                if (copy.HasProperty("_Mode")) copy.SetFloat("_Mode", 2f);
                copy.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                copy.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (copy.HasProperty("_ZWrite")) copy.SetInt("_ZWrite", 0);
                copy.DisableKeyword("_ALPHATEST_ON"); copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                copy.EnableKeyword("_ALPHABLEND_ON"); copy.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (copy.shader.name != "Sprites/Default")
                throw new InvalidDataException("The existing item-card shader has no supported physical-card fade channel.");
        }
    }

    internal static void Dispose(GameObject body)
    {
        if (!Materials.TryGetValue(body, out Owned state)) return;
        Materials.Remove(body);
        foreach (Material copy in state.Copies) if (copy != null) Object.Destroy(copy);
    }

    /// <summary>Unity cloning copies the current mesh, not CardMesh's live consumer registry.
    /// Re-register each inert template/observer so a later original item silhouette updates
    /// its rim at the same time as local samples. Never add controllers or alter topology.</summary>
    internal static void RebindClone(string address, GameObject clone)
    {
        if (address != "merchant.cardbody" && address != "merchant.cardbody|") return;
        MeshFilter? filter = clone.GetComponent<MeshFilter>();
        if (filter == null) throw new InvalidDataException("The physical item-card template has no mesh filter.");
        CardMesh.AttachBody(filter, CardBodyKind.Item, 1f, 1f);
    }
}

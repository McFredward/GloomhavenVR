// Frozen MB492 capture oracle. Compare complete output, including zero/unused lanes,
// against the pre-optimization production algorithm. Runtime rendering remains Unity-owned.
using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Serialized native CardEffects roles survive cloning even before Initialize ran.</summary>
internal sealed class ReferenceCardAppearanceBindings
{
    private static readonly string[] RoleFields = { "_headerImage", "_topButton", "_bottomAction", "_topDefAction", "_botDefAction",
        "_topDefActionIcon", "_botDefActionIcon", "_header", "_topDefActionTxt", "_bottomDefActionTxt", "_initiativeText", "_uiFxOverlay" };
    private static readonly FieldInfo?[] Fields = Array.ConvertAll(RoleFields, name => typeof(CardEffects).GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    internal static readonly string[] FloatNames = { "_GreyOut", "_Flow", "_Dissolve", "_Burn", "_Flow_Offset", "_Flow_Speed",
        "_Dissolve_VerticalGradient", "_FXAnim", "_FlowSpeed", "_OffsetStrength", "_ThinHighlights", "_ThinHighlight_Min",
        "_ThinHighlight_Max", "_Flow_NoiseTiling", "_Glow" };
    internal static readonly int[] FloatIds = Array.ConvertAll(FloatNames, Shader.PropertyToID);
    internal static readonly int BurnTint = Shader.PropertyToID("_Burn_ColourTint"), FlameTint = Shader.PropertyToID("_TintColor"),
        Noise = Shader.PropertyToID("_AnimNoise_Mask"), Particle = Shader.PropertyToID("_ParticleTexture");
    internal readonly Graphic?[] Graphics = new Graphic?[12];
    internal readonly System.Collections.Generic.Dictionary<uint, CanvasGroup> Groups = new();
    internal readonly Transform Root;
    internal readonly Texture? BurnTexture, GhostTexture;
    internal readonly Material? LowMaterial;
    private static Material?[]? _authoredMaterials;
    private static readonly FieldInfo? LowMaterialField = typeof(CardEffects).GetField("_lowMaterial",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static void ResetAssets() => _authoredMaterials = null;
    internal static Material? AuthoredMaterial(byte role)
    {
        if (role >= 7) return null;
        if (_authoredMaterials == null || _authoredMaterials[role] == null)
        {
            // PersistentData.CreateAbilityCard1 uses this exact original asset. Read its
            // serialized materials without instantiating it or running CardEffects.Awake.
            var manager = AssetBundleManager.Instance;
            GameObject? prefab = manager != null ? manager.LoadAssetFromBundle<GameObject>("misc_gui", "AbilityCard", "gui") : null;
            CardEffects? effects = prefab != null ? prefab.GetComponentInChildren<CardEffects>(true) : null;
            if (effects == null) return null;
            var materials = new Material?[7];
            for (int i = 0; i < materials.Length; i++) materials[i] = (Fields[i]?.GetValue(effects) as Graphic)?.material;
            _authoredMaterials = materials;
        }
        return _authoredMaterials[role];
    }
    internal ReferenceCardAppearanceBindings(CardEffects effects)
    {
        Root = effects.transform;
        LowMaterial = LowMaterialField?.GetValue(effects) as Material;
        for (int i = 0; i < Fields.Length; i++) Graphics[i] = Fields[i]?.GetValue(effects) as Graphic;
        BurnTexture = effects.overlayFrameBurn; GhostTexture = effects.overlayFrameGhost;
        RefreshGroups();
    }
    internal void RefreshGroups()
    {
        Groups.Clear();
        foreach (CanvasGroup group in Root.GetComponentsInChildren<CanvasGroup>(true))
        {
            uint key = GroupKey(group.transform, Root);
            if (Groups.ContainsKey(key)) throw new InvalidOperationException("Duplicate original card group binding.");
            Groups.Add(key, group);
        }
    }
    internal CardAppearanceNode[] Capture(bool detachedRoot = false)
    {
        RefreshGroups();
        var nodes = new System.Collections.Generic.List<CardAppearanceNode>();
        for (byte role = 0; role < Graphics.Length; role++)
        {
            Graphic? graphic = Graphics[role];
            if (graphic == null) continue;
            var node = new CardAppearanceNode { Role = role, Flags = (byte)((VisibleInCard(graphic.transform, Root, detachedRoot) ? 1 : 0)
                | (graphic.enabled ? 2 : 0) | (graphic is TextMeshProUGUI tmp && tmp.enableVertexGradient ? 4 : 0)) };
            Put(node.Values, 0, graphic.color);
            Color renderer = graphic.canvasRenderer.GetColor();
            Put(node.Values, 4, renderer);
            Material material = graphic.material;
            // SimplifiedUI replaces every affected native material with _lowMaterial in
            // CardEffects.Initialize. The actual assigned shader identifies that variant;
            // the receiver's own SimplifiedUI preference is never consulted.
            if (role < 7 && LowMaterial != null && material.shader == LowMaterial.shader) node.Flags |= 16;
            uint allowed = CardAppearanceNode.AllowedMask(role);
            for (int f = 0; f < FloatIds.Length; f++) if ((allowed & (1u << f)) != 0 && material.HasProperty(FloatIds[f]))
            { node.Mask |= 1u << f; node.Values[8 + f] = material.GetFloat(FloatIds[f]); }
            if ((allowed & (1u << 15)) != 0 && material.HasProperty(BurnTint))
            { node.Mask |= 1u << 15; Put(node.Values, 23, material.GetColor(BurnTint)); }
            if ((allowed & (1u << 16)) != 0 && material.HasProperty(FlameTint))
            { node.Mask |= 1u << 16; Put(node.Values, 27, material.GetColor(FlameTint)); }
            if ((allowed & (1u << 17)) != 0 && material.HasProperty(Noise))
            { node.Mask |= 1u << 17; Vector2 scale = material.GetTextureScale(Noise); node.Values[31] = scale.x; node.Values[32] = scale.y; }
            // The two authored flame textures have a fixed role, never a transmitted asset name.
            if (role == 11 && material.HasProperty(Particle) && ReferenceEquals(material.GetTexture(Particle), GhostTexture))
                node.Flags |= 8;
            nodes.Add(node);
        }
        nodes.AddRange(CaptureGroups(0, 8, detachedRoot));
        return nodes.ToArray();
    }
    // Native action layouts contain more than eight CanvasGroups. The first eight keep their
    // original record58 roles; additive69 carries every remaining group. Never throw during
    // clone construction because a wire layout was smaller than the game's real widget tree.
    internal CardAppearanceNode[] CaptureExtraGroups(bool detachedRoot = false)
        => CaptureGroups(8, int.MaxValue, detachedRoot);

    private CardAppearanceNode[] CaptureGroups(int first, int count, bool detachedRoot)
    {
        var keys = new System.Collections.Generic.List<uint>(Groups.Keys); keys.Sort();
        var nodes = new System.Collections.Generic.List<CardAppearanceNode>();
        for (int index = first; index < keys.Count && index - first < count; index++)
        {
            uint key = keys[index];
            CanvasGroup group = Groups[key];
            var node = new CardAppearanceNode { Role = (byte)(first == 0 ? 12 + index : 12), Binding = key,
                Flags = (byte)((group.gameObject.activeSelf ? 1 : 0) | (group.enabled ? 2 : 0) | (group.ignoreParentGroups ? 4 : 0)) };
            node.Values[0] = group.alpha;
            // A local fallback flight owns a new root pose/visibility after the original widget
            // was parked. Preserve its inner native holders and output, not the parked root.
            if (detachedRoot && ReferenceEquals(group.transform, Root)) { node.Flags |= 1; node.Values[0] = 1f; }
            nodes.Add(node);
        }
        return nodes.ToArray();
    }
    // activeSelf alone misses disabled intermediate holders (including the native flame holder).
    // Capture the visible result inside this card; receiver-local board parents are not authority.
    private static bool VisibleInCard(Transform transform, Transform root, bool detachedRoot)
    {
        for (Transform? t = transform; t != null; t = t.parent)
        {
            if (ReferenceEquals(t, root) && detachedRoot) return true;
            if (!t.gameObject.activeSelf) return false;
            if (ReferenceEquals(t, root)) return true;
        }
        return false;
    }
    private static uint GroupKey(Transform transform, Transform root)
    {
        uint hash = 2166136261;
        for (Transform? t = transform; t != null && !ReferenceEquals(t, root); t = t.parent)
        {
            foreach (char c in t.name) hash = (hash ^ c) * 16777619;
            hash = (hash ^ (uint)t.GetSiblingIndex()) * 16777619;
        }
        return hash == 0 ? 1u : hash;
    }
    internal static void Put(float[] values, int offset, Color color)
    { values[offset] = color.r; values[offset + 1] = color.g; values[offset + 2] = color.b; values[offset + 3] = color.a; }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>Copies evaluated presentation without instantiating native scripts or Animator
/// controllers. Animation events and StateMachineBehaviours must never run on a home ghost.</summary>
[DefaultExecutionOrder(29950)]
internal sealed class FigureVisualMirror : MonoBehaviour
{
    internal readonly struct AnimatorPose
    {
        internal AnimatorPose(Animator animator)
        {
            Name = animator.name;
            States = new AnimatorStateInfo[animator.layerCount];
            NextStates = new AnimatorStateInfo[animator.layerCount];
            Transitions = new AnimatorTransitionInfo[animator.layerCount];
            Weights = new float[animator.layerCount];
            for (int i = 0; i < States.Length; i++)
            {
                States[i] = animator.GetCurrentAnimatorStateInfo(i);
                NextStates[i] = animator.GetNextAnimatorStateInfo(i);
                Transitions[i] = animator.GetAnimatorTransitionInfo(i);
                Weights[i] = animator.GetLayerWeight(i);
            }
            Parameters = animator.parameters;
            Values = new float[Parameters.Length];
            Integers = new int[Parameters.Length];
            Booleans = new bool[Parameters.Length];
            for (int i = 0; i < Parameters.Length; i++)
            {
                AnimatorControllerParameter p = Parameters[i];
                if (p.type == AnimatorControllerParameterType.Float) Values[i] = animator.GetFloat(p.nameHash);
                else if (p.type == AnimatorControllerParameterType.Int) Integers[i] = animator.GetInteger(p.nameHash);
                else if (p.type == AnimatorControllerParameterType.Bool) Booleans[i] = animator.GetBool(p.nameHash);
                // Triggers are consumed events, never replayed by a visual copy.
            }
        }
        internal readonly string Name;
        internal readonly AnimatorStateInfo[] States, NextStates;
        internal readonly AnimatorTransitionInfo[] Transitions;
        internal readonly float[] Weights, Values;
        internal readonly int[] Integers;
        internal readonly bool[] Booleans;
        internal readonly AnimatorControllerParameter[] Parameters;
    }

    private readonly List<(Transform Source, Transform Copy)> _transforms = new();
    private readonly List<(Renderer Source, Renderer Copy, int Shapes, bool Visibility)> _renderers = new();
    private bool _home;
    internal AnimatorPose[] InitialAnimatorPoses { get; private set; } = System.Array.Empty<AnimatorPose>();

    /// <summary>A complete visual hierarchy with original sibling order/bone references. It has
    /// no native component whose Awake, animation callback or physics could affect gameplay.</summary>
    internal static GameObject CloneVisual(GameObject source, Vector3 position, Quaternion rotation,
                                            Vector3 scale, out FigureVisualMirror mirror)
    {
        var root = new GameObject("VRFigureGhost");
        root.SetActive(false);
        var map = new Dictionary<Transform, Transform>();
        CopyTree(source.transform, root.transform, map);
        root.transform.SetPositionAndRotation(position, rotation);
        root.transform.localScale = scale;
        mirror = root.AddComponent<FigureVisualMirror>();
        mirror._home = true;
        foreach (KeyValuePair<Transform, Transform> pair in map)
        {
            if (pair.Key != source.transform) mirror._transforms.Add((pair.Key, pair.Value));
            if (ModOwned(pair.Key, source.transform)) continue;
            foreach (Renderer renderer in pair.Key.GetComponents<Renderer>())
            {
                Renderer? copy = CopyRenderer(renderer, pair.Value, map);
                if (copy != null) mirror.Bind(renderer, copy);
            }
        }
        // Retain the native LOD topology. Disabled/coarse source renderer masks are additionally
        // copied each frame so an owned performance mask cannot reappear through the ghost.
        foreach (KeyValuePair<Transform, Transform> pair in map)
        {
            foreach (LODGroup original in pair.Key.GetComponents<LODGroup>())
            {
                LOD[] table = original.GetLODs();
                for (int i = 0; i < table.Length; i++)
                {
                    var renderers = new List<Renderer>();
                    foreach (Renderer r in table[i].renderers)
                        foreach (var binding in mirror._renderers)
                            if (binding.Source == r) renderers.Add(binding.Copy);
                    table[i].renderers = renderers.ToArray();
                }
                var group = pair.Value.gameObject.AddComponent<LODGroup>();
                group.SetLODs(table);
                group.localReferencePoint = original.localReferencePoint;
                group.size = original.size;
                group.fadeMode = original.fadeMode;
                group.animateCrossFading = original.animateCrossFading;
                group.enabled = original.enabled;
            }
        }
        Animator[] animators = source.GetComponentsInChildren<Animator>(true);
        var poses = new List<AnimatorPose>(animators.Length);
        foreach (Animator animator in animators)
            if (animator.runtimeAnimatorController != null && animator.isInitialized)
                poses.Add(new AnimatorPose(animator));
        mirror.InitialAnimatorPoses = poses.ToArray();
        mirror.Sync();
        root.SetActive(true);
        return root;
    }

    private static void CopyTree(Transform source, Transform copy, Dictionary<Transform, Transform> map)
    {
        copy.gameObject.layer = source.gameObject.layer;
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        map.Add(source, copy);
        for (int i = 0; i < source.childCount; i++)
        {
            Transform child = source.GetChild(i);
            var go = new GameObject(child.name);
            go.SetActive(child.gameObject.activeSelf);
            go.transform.SetParent(copy, false);
            CopyTree(child, go.transform, map);
        }
    }

    private static Renderer? CopyRenderer(Renderer source, Transform parent,
                                          Dictionary<Transform, Transform> map)
    {
        Renderer copy;
        if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
        {
            var target = parent.gameObject.AddComponent<SkinnedMeshRenderer>();
            target.sharedMesh = skinned.sharedMesh;
            Transform[] bones = skinned.bones;
            for (int i = 0; i < bones.Length; i++)
                bones[i] = bones[i] != null && map.TryGetValue(bones[i], out Transform twin) ? twin : null!;
            target.bones = bones;
            target.rootBone = skinned.rootBone != null && map.TryGetValue(skinned.rootBone, out Transform root)
                ? root : null;
            target.localBounds = skinned.localBounds;
            target.quality = skinned.quality;
            target.updateWhenOffscreen = skinned.updateWhenOffscreen;
            FigureOverlay.CopyBlendShapeWeights(skinned, target);
            copy = target;
        }
        else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter)
                 && filter.sharedMesh != null)
        {
            parent.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            copy = parent.gameObject.AddComponent<MeshRenderer>();
        }
        else return null; // Particle/trail/line systems are not a surface ghost.
        copy.sharedMaterials = source.sharedMaterials;
        copy.enabled = source.enabled;
        copy.forceRenderingOff = source.forceRenderingOff;
        copy.sortingLayerID = source.sortingLayerID;
        copy.sortingOrder = source.sortingOrder;
        copy.shadowCastingMode = source.shadowCastingMode;
        copy.receiveShadows = source.receiveShadows;
        copy.lightProbeUsage = source.lightProbeUsage;
        copy.reflectionProbeUsage = source.reflectionProbeUsage;
        return copy;
    }

    internal void Bind(Renderer source, Renderer copy)
    {
        int shapes = source is SkinnedMeshRenderer skin && skin.sharedMesh != null
            ? skin.sharedMesh.blendShapeCount : 0;
        _renderers.Add((source, copy, shapes, true));
    }

    internal void Retain(List<Renderer> retained, Transform? ring)
    {
        for (int i = _renderers.Count - 1; i >= 0; i--)
        {
            var pair = _renderers[i];
            if (!retained.Contains(pair.Copy)) { _renderers.RemoveAt(i); continue; }
            if (ring != null && (pair.Copy.transform == ring || pair.Copy.transform.IsChildOf(ring)))
                _renderers[i] = (pair.Source, pair.Copy, pair.Shapes, false);
        }
        // A preserved home ring must not follow suppression of the live in-hand ring.
        _transforms.RemoveAll(pair => pair.Copy == null || ModOwned(pair.Copy, transform)
            || (ring != null && (pair.Copy == ring || pair.Copy.IsChildOf(ring))));
    }

    private static bool ModOwned(Transform node, Transform root)
    {
        for (Transform? current = node; current != null && current != root; current = current.parent)
            if (current.name.StartsWith(Core.VRLayers.ModOwnedNamePrefix, System.StringComparison.Ordinal)) return true;
        return false;
    }

    internal void BindDepth(Renderer ghost, Renderer depth)
    {
        for (int i = 0; i < _renderers.Count; i++)
            if (_renderers[i].Copy == ghost) { Bind(_renderers[i].Source, depth); return; }
    }

    /// <summary>Live highlight parts already share native bones; static props, blend shapes and
    /// runtime visibility need the same evaluated-presentation binding as a home ghost.</summary>
    internal static void BindHighlight(Renderer source, Renderer copy)
    {
        var mirror = copy.gameObject.AddComponent<FigureVisualMirror>();
        mirror.Bind(source, copy);
        mirror.Sync();
    }

    private void LateUpdate() => Sync();

    internal void Sync()
    {
        foreach (var pair in _transforms)
        {
            if (pair.Source == null || pair.Copy == null) continue;
            pair.Copy.localPosition = pair.Source.localPosition;
            pair.Copy.localRotation = pair.Source.localRotation;
            pair.Copy.localScale = pair.Source.localScale;
            if (pair.Copy.gameObject.activeSelf != pair.Source.gameObject.activeSelf)
                pair.Copy.gameObject.SetActive(pair.Source.gameObject.activeSelf);
        }
        foreach (var pair in _renderers)
        {
            if (pair.Copy == null) continue;
            if (pair.Source == null) { pair.Copy.enabled = false; continue; }
            if (pair.Visibility)
            {
                pair.Copy.enabled = pair.Source.enabled;
                pair.Copy.forceRenderingOff = pair.Source.forceRenderingOff;
            }
            if (!_home)
            {
                pair.Copy.transform.SetPositionAndRotation(pair.Source.transform.position, pair.Source.transform.rotation);
                FigureOverlay.MatchCloneWorldScale(pair.Copy.transform, pair.Copy.transform.parent, pair.Source.transform);
            }
            if (pair.Shapes > 0 && pair.Source is SkinnedMeshRenderer from && pair.Copy is SkinnedMeshRenderer to)
                for (int i = 0; i < pair.Shapes; i++) to.SetBlendShapeWeight(i, from.GetBlendShapeWeight(i));
        }
        // The ghost owner is the sole root-pose author. In particular, an updated board/home
        // transform must not be overwritten by a second grab-time pose cached in this mirror.
    }
}

/// <summary>Keep each native cutout silhouette and UV mapping when replacing its lighting with
/// a tint. Mask UVs are separate from the highlight's animated MainTex scroll.</summary>
internal static class FigureOverlayMasks
{
    private static readonly int UseMask = Shader.PropertyToID("_UseAlphaMask");
    private static readonly int MaskTex = Shader.PropertyToID("_AlphaMaskTex");
    private static readonly int MaskST = Shader.PropertyToID("_AlphaMaskTex_ST");
    private static readonly int Cutoff = Shader.PropertyToID("_AlphaMaskCutoff");

    internal static void Apply(Renderer source, Renderer target, Material[] materials)
    {
        int count = target.sharedMaterials.Length;
        for (int i = 0; i < count; i++)
        {
            // Unity reuses the last source material when there are fewer materials than
            // submeshes. Keep its cutout on every such submesh, rather than tinting the rest solid.
            Material? material = materials.Length > 0 ? materials[Mathf.Min(i, materials.Length - 1)] : null;
            var block = new MaterialPropertyBlock();
            if (material != null && IsCutout(material))
            {
                string property = material.HasProperty("_Diffuse") ? "_Diffuse" : "_MainTex";
                Texture? texture = material.HasProperty(property) ? material.GetTexture(property) : null;
                if (texture != null)
                {
                    Vector2 scale = material.GetTextureScale(property), offset = material.GetTextureOffset(property);
                    block.SetFloat(UseMask, 1f);
                    block.SetTexture(MaskTex, texture);
                    block.SetVector(MaskST, new Vector4(scale.x, scale.y, offset.x, offset.y));
                    block.SetFloat(Cutoff, material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f);
                }
            }
            target.SetPropertyBlock(block, i);
        }
        target.enabled = source.enabled;
        target.forceRenderingOff = source.forceRenderingOff;
    }

    private static bool IsCutout(Material material)
    {
        string tag = material.GetTag("RenderType", false, string.Empty);
        return (tag.Contains("Cutout") || tag.Contains("Foliage") || tag.Contains("Grass") || tag.Contains("TreeLeaf"))
            && (!material.HasProperty("_Diffuse_Alpha_On") || material.GetFloat("_Diffuse_Alpha_On") > 0f);
    }

    internal static void Copy(Renderer source, Renderer target, int submeshes)
    {
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < submeshes; i++)
        {
            source.GetPropertyBlock(block, i);
            target.SetPropertyBlock(block, i);
        }
        target.enabled = source.enabled;
        target.forceRenderingOff = source.forceRenderingOff;
    }
}

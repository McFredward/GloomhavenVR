using System;
using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Cached conservative skin envelope, evaluated from the actual native bone transforms.
/// Build609's flying Drake bar crossed a wing, while its sleeping bar retained the authored
/// 2.10wu flight floor. Head-only, lifetime maxima and a pose ratio keyed by mesh cannot describe
/// both. The original sleeping skin reaches 0.967–0.984wu and the flying skin 2.317–3.012wu.
///
/// Each original vertex is enclosed in the local bounds of EVERY bone with positive weight.
/// Its skinned world position is a convex combination of those transformed points; their union
/// therefore encloses the skin in every evaluated pose, including native animation blends.
/// One preparation bake recovers bind vertices on non-readable meshes. No bake, vertices,
/// hierarchy search, renderer/LOD inventory or allocation belongs to the steady height read.
/// </summary>
internal sealed class ActorBarPose
{
    private readonly struct Bone
    {
        internal readonly Transform Transform;
        internal readonly Bounds Local;
        internal Bone(Transform transform, Bounds local) { Transform = transform; Local = local; }
    }
    private sealed class Profile
    {
        internal Bounds[] Boxes = Array.Empty<Bounds>();
        internal bool[] Used = Array.Empty<bool>();
    }
    private sealed class Skin
    {
        internal SkinnedMeshRenderer Renderer = null!;
        internal Mesh Original = null!;
        internal Transform[] Bones = Array.Empty<Transform>();
        internal ScenarioFigureMeshBank.Record? Owner;
        internal bool SourceMatches()
        {
            if (Renderer == null) return false;
            Mesh current = Renderer.sharedMesh;
            if (current == Original) return true;
            // An initially native100% surface may acquire an owned derivative later. Resolve
            // its ownership token only at that change, never inventory actors every frame.
            Owner ??= ScenarioFigureDetailBudget.OriginalRecordFor(Renderer);
            return Owner != null && Owner.Original == Original && Owner.UsesDerivative;
        }
    }
    private const int VertexLimit = 60000;
    private const int ProfileLimit = 256;
    private static readonly Dictionary<Mesh, Profile> Profiles = new();
    private static readonly HashSet<Mesh> Refused = new();
    private static readonly List<Vector3> Vertices = new(8192);
    private static Mesh? Bake;
    private static SkinnedMeshRenderer? Baker;
    private readonly Bone[] _bones;
    private readonly Transform _root;
    private readonly Transform _head;
    private readonly Skin[] _skins;
    private ActorBarPose(Transform root, Transform head, Bone[] bones, Skin[] skins)
    { _root = root; _head = head; _bones = bones; _skins = skins; }
    internal int BoneCount => _bones.Length;

    internal static ActorBarPose? Capture(GameObject root, Transform head)
    {
        if (root == null || head == null || !head.IsChildOf(root.transform)) return null;
        var boxes = new Dictionary<Transform, Bounds>();
        var skins = new List<Skin>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer == null || renderer.isPartOfStaticBatch
                || renderer.GetComponentInParent<Canvas>(true) != null
                || renderer.GetComponentInParent<FigureVisualMirror>(true) != null
                || renderer.GetComponentInParent<ParticleSystem>(true) != null
                || renderer.GetComponent<Cloth>() != null)
                continue;
            bool mod = false;
            for (Transform? t = renderer.transform; t != null && t != root.transform; t = t.parent)
                if (t.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal)
                    || t.name.StartsWith("VR_", StringComparison.Ordinal)
                    || t.name.StartsWith("P_", StringComparison.Ordinal)) { mod = true; break; }
            if (mod) continue;
            if (renderer is SkinnedMeshRenderer skin)
            {
                ScenarioFigureMeshBank.Record? owner = ScenarioFigureDetailBudget.OriginalRecordFor(skin);
                Mesh? source = owner?.Original ?? skin.sharedMesh;
                Transform[] bones = skin.bones;
                if (bones.Length == 0) continue; // a skinned FX shell is not an animated body
                // Positive skeleton ownership: native body surfaces share the tracked head's
                // skeleton. Action effects with their own bones and arbitrary rigid meshes
                // below the actor wrapper are not the creature's body. No name/model offsets.
                bool ownsHead = skin.rootBone != null && head.IsChildOf(skin.rootBone);
                if (!ownsHead)
                    foreach (Transform bone in bones) if (bone == head) { ownsHead = true; break; }
                if (!ownsHead) continue;
                if (source == null) return null; // positively owned native body is still arriving
                if (source.vertexCount == 0) continue; // authored empty far-cull LOD
                Profile? profile = Prepare(skin, source, bones);
                if (profile == null) return null; // a partial body is not a trustworthy lower anchor
                for (int i = 0; i < profile.Used.Length; i++)
                {
                    if (!profile.Used[i]) continue;
                    if (i >= bones.Length || bones[i] == null) return null;
                    Bounds local = profile.Boxes[i];
                    if (boxes.TryGetValue(bones[i], out Bounds earlier))
                    {
                        earlier.Encapsulate(local.min); earlier.Encapsulate(local.max); local = earlier;
                    }
                    boxes[bones[i]] = local;
                }
                skins.Add(new Skin { Renderer = skin, Original = source, Bones = bones, Owner = owner });
            }
        }
        if (boxes.Count == 0) return null; // plain props retain the established body/arch policy
        var bound = new Bone[boxes.Count];
        int index = 0;
        foreach (KeyValuePair<Transform, Bounds> box in boxes) bound[index++] = new Bone(box.Key, box.Value);
        return new ActorBarPose(root.transform, head, bound, skins.ToArray());
    }

    internal bool Matches(GameObject root, Transform head) => root != null
        && root.transform == _root && head == _head;

    // Unity exposes the palette as an allocating array. Validate only cached body skins at the
    // existing staggered slow cadence; no hierarchy/LOD discovery or geometry read is involved.
    internal bool PaletteMatches()
    {
        foreach (Skin skin in _skins)
        {
            if (!skin.SourceMatches()) return false;
            Transform[] current = skin.Renderer.bones;
            if (current.Length != skin.Bones.Length) return false;
            for (int i = 0; i < current.Length; i++) if (current[i] != skin.Bones[i]) return false;
        }
        return true;
    }

    internal bool TryTop(out float top)
    {
        top = float.NegativeInfinity;
        foreach (Skin skin in _skins) if (!skin.SourceMatches()) return false;
        foreach (Bone bone in _bones)
        {
            if (_root == null || bone.Transform == null || !bone.Transform.IsChildOf(_root)) return false;
            top = Mathf.Max(top, Top(bone.Transform.localToWorldMatrix, bone.Local));
        }
        return !float.IsNaN(top) && !float.IsInfinity(top);
    }

    private static float Top(in Matrix4x4 matrix, in Bounds box)
    {
        Vector3 c = box.center, e = box.extents;
        return matrix.m10 * c.x + matrix.m11 * c.y + matrix.m12 * c.z + matrix.m13
            + Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z;
    }

    internal static float Offset(float top, float trackY, float baseY, float userOffset)
    {
        float height = Mathf.Max(top - baseY, 0.01f);
        float needed = top - trackY + Mathf.Max(0.05f, 0.12f * height);
        // A verified, scaled native body may exceed the legacy 6wu guard. Never clamp its bar
        // inside that body. The player's offset remains last; its old 0..6wu limit still applies
        // to ordinary figures. A native HeadBoneStatic point captured in flight can sit above
        // the sleeping body: a signed body offset must cross below that point. The lower guard
        // is therefore the actor's base, and a negative user adjustment stays last.
        float upper = needed > 6f ? needed + Mathf.Max(0f, userOffset) : 6f;
        return Mathf.Clamp(needed + userOffset, baseY - trackY, upper);
    }

    internal static float Follow(float current, float needed, float deltaSeconds) => needed >= current
        ? needed // abrupt native waking must clear the new evaluated body on this same frame
        : Mathf.Lerp(current, needed, 1f - Mathf.Exp(-8f * Mathf.Max(0f, deltaSeconds)));

    private static Profile? Prepare(SkinnedMeshRenderer skin, Mesh source, Transform[] bones)
    {
        if (Profiles.TryGetValue(source, out Profile known)) return known;
        if (Refused.Contains(source) || Profiles.Count >= ProfileLimit) return null;
        try
        {
            // Native body meshes retain skinning metadata even when their vertex channel is not
            // script-readable. A private, disabled baker uses the ORIGINAL fine mesh, never swaps
            // the game's sharedMesh and never owns an Animator, native script, collider or material.
            BoneWeight[] weights = source.boneWeights;
            Matrix4x4[] bind = source.bindposes;
            if (source.vertexCount > VertexLimit || weights.Length != source.vertexCount
                || bind.Length == 0 || bind.Length > bones.Length || source.blendShapeCount != 0)
                return Refuse(source); // unsupported deformation keeps the existing fallback policy
            for (int i = 0; i < bind.Length; i++)
                if (bones[i] == null) return null; // async native skeleton arrival is not a source defect
            if (Baker == null)
            {
                var host = new GameObject("GloomhavenVR.BarPoseBaker") { hideFlags = HideFlags.HideAndDontSave };
                host.SetActive(false); Baker = host.AddComponent<SkinnedMeshRenderer>(); Baker.enabled = false;
                Bake = new Mesh { name = "GloomhavenVR.BarPoseScratch" };
            }
            // Neutral world space is essential: default BakeMesh under a rotated, nonuniformly
            // scaled renderer ancestor can fold its lossy scale into CPU output. A standalone
            // identity baker recovers the same original bind points across those native hierarchies.
            // It reads live bones; no native transform, skin, quality or Animator is changed.
            Baker.transform.SetParent(null, worldPositionStays: false);
            Baker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Baker.transform.localScale = Vector3.one;
            Baker.sharedMesh = source; Baker.bones = bones; Baker.rootBone = skin.rootBone;
            Baker.quality = SkinQuality.Bone4; Baker.BakeMesh(Bake!, useScale: false);
            Bake!.GetVertices(Vertices);
            if (Vertices.Count != source.vertexCount) return null; // transient native bake failure
            Matrix4x4 world = Baker.transform.localToWorldMatrix;
            var matrices = new Matrix4x4[bind.Length];
            for (int i = 0; i < bind.Length; i++)
            {
                matrices[i] = bones[i].localToWorldMatrix * bind[i];
            }
            var profile = new Profile { Boxes = new Bounds[bind.Length], Used = new bool[bind.Length] };
            for (int i = 0; i < Vertices.Count; i++)
            {
                BoneWeight weight = weights[i];
                Matrix4x4 deform = default;
                Add(ref deform, matrices, weight.boneIndex0, weight.weight0);
                Add(ref deform, matrices, weight.boneIndex1, weight.weight1);
                Add(ref deform, matrices, weight.boneIndex2, weight.weight2);
                Add(ref deform, matrices, weight.boneIndex3, weight.weight3);
                if (Mathf.Abs(deform.determinant) < 1e-10f) return null; // retry a non-singular native pose
                Vector3 vertex = deform.inverse.MultiplyPoint3x4(world.MultiplyPoint3x4(Vertices[i]));
                if (!Finite(vertex.x) || !Finite(vertex.y) || !Finite(vertex.z)) return null;
                Enclose(profile, bind, vertex, weight.boneIndex0, weight.weight0);
                Enclose(profile, bind, vertex, weight.boneIndex1, weight.weight1);
                Enclose(profile, bind, vertex, weight.boneIndex2, weight.weight2);
                Enclose(profile, bind, vertex, weight.boneIndex3, weight.weight3);
            }
            Profiles[source] = profile;
            return profile;
        }
        catch (Exception error)
        {
            if (Refused.Count < ProfileLimit && Refused.Add(source)) VRLog.Note("WorldUI", "BAR POSE envelope unavailable for '"
                + source.name + "' (" + error.GetType().Name + "); existing native anchor policy retained.");
            return null;
        }
        finally
        {
            Vertices.Clear();
            if (Baker != null)
            {
                Baker.sharedMesh = null; Baker.bones = Array.Empty<Transform>(); Baker.rootBone = null;
                Baker.transform.SetParent(null);
            }
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Profile? Refuse(Mesh source)
    { if (Refused.Count < ProfileLimit) Refused.Add(source); return null; }
    private static void Add(ref Matrix4x4 result, Matrix4x4[] matrices, int index, float weight)
    {
        if (weight <= 0f) return;
        Matrix4x4 source = matrices[index];
        for (int i = 0; i < 16; i++) result[i] += source[i] * weight;
    }
    private static void Enclose(Profile profile, Matrix4x4[] bind, Vector3 vertex, int index, float weight)
    {
        if (weight <= 0f) return;
        Vector3 local = bind[index].MultiplyPoint3x4(vertex);
        if (profile.Used[index]) profile.Boxes[index].Encapsulate(local);
        else { profile.Boxes[index] = new Bounds(local, Vector3.zero); profile.Used[index] = true; }
    }

    internal static void Reset()
    {
        Profiles.Clear(); Refused.Clear(); Vertices.Clear();
        if (Baker != null) UnityEngine.Object.Destroy(Baker.gameObject);
        if (Bake != null) UnityEngine.Object.Destroy(Bake);
        Baker = null; Bake = null;
    }
}

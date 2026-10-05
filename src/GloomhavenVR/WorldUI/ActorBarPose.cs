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
/// Build612's Frame test confirms that following each evaluated wing beat makes this envelope
/// bob. Native loop clips now get one bounded preparation pass on a transform-only private
/// skeleton. Their complete cycle bounds stay fixed until the native state changes; non-loop
/// actions and waking/sleeping blends still follow the evaluated original body. The latch is
/// STATE-scoped, never a lifetime maximum that would leave a sleeping Drake's bar in flight.
/// One preparation bake recovers bind vertices on non-readable meshes. No bake, vertices,
/// hierarchy search, renderer/LOD inventory or allocation belongs to the steady height read.
/// </summary>
internal sealed class ActorBarPose
{
    private readonly struct Bone
    {
        internal readonly Transform Transform;
        internal readonly Transform? Parent;
        internal readonly Bounds Local;
        internal Bone(Transform transform, Bounds local)
        { Transform = transform; Parent = transform.parent; Local = local; }
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
    private readonly Animator? _animator;
    private readonly Dictionary<AnimationClip, Bounds> _loops = new();
    private readonly List<AnimatorClipInfo> _clips = new(4);
    private int _loopState;
    private bool _hasLoop;
    private float _loopTop;
    private Vector3 _loopScale;
    private Quaternion _loopRotation;
    private int _cycleReports;
    private bool _quietRig = true;
    private readonly List<Component> _liveAudit = new(4);
    private readonly HashSet<AnimationClip> _eventFreeIdleLoops = new();
    private RuntimeAnimatorController? _preparedController;
    private string _auditRefusal = "";
    private readonly Dictionary<int, bool> _stateAudits = new();
    private bool _stateSafe = true;
    private bool _hasNoStateBehaviours;
    private float _nextVerification;
    private float _verifiedRelativeTop;
    private int _verifications, _skippedVerifications;
    private ActorBarPose(Transform root, Transform head, Bone[] bones, Skin[] skins)
    {
        _root = root; _head = head; _bones = bones; _skins = skins;
        _animator = head.GetComponentInParent<Animator>(true);
        if (_animator != null && _animator.transform.IsChildOf(root)) PrepareLoops();
    }
    internal int BoneCount => _bones.Length;
    internal int LoopCount => _loops.Count;
    internal int VerificationCount => _verifications;
    internal int SkippedVerificationCount => _skippedVerifications;
    internal bool SparseEligible => _root != null && _quietRig && _stateSafe
        && NativeActorPoseAudit.StillSafe(_liveAudit, _root);
    internal string AuditRefusal => _auditRefusal;
    internal Animator? NativeAnimator => _animator;
    internal GameObject NativeRoot => _root.gameObject;

    internal bool CopyIdleSkinSources(List<SkinnedMeshRenderer> result)
    {
        result.Clear();
        if (!IsEventFreeNativeIdle()) return false;
        foreach (Skin skin in _skins)
        {
            if (!skin.SourceMatches()) { result.Clear(); return false; }
            if (skin.Renderer != null && skin.Renderer.enabled && skin.Renderer.gameObject.activeInHierarchy)
                result.Add(skin.Renderer);
        }
        return true;
    }

    internal bool IsEventFreeNativeIdle()
    {
        if (!SparseEligible || _animator == null || !_animator.isActiveAndEnabled
            || _animator.runtimeAnimatorController != _preparedController || _animator.layerCount != 1
            || _animator.IsInTransition(0) || _animator.GetNextAnimatorStateInfo(0).fullPathHash != 0)
            return false;
        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        if (!state.loop || !NativeActorPoseAudit.IsIdleState(state)) return false;
        if (!StateIsQuiet(state.fullPathHash)) return false;
        _clips.Clear(); _animator.GetCurrentAnimatorClipInfo(0, _clips);
        return _clips.Count == 1 && _clips[0].clip != null
            && _eventFreeIdleLoops.Contains(_clips[0].clip);
    }

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
        if (_animator != null && _animator.runtimeAnimatorController != _preparedController) return false;
        foreach (Skin skin in _skins)
        {
            if (!skin.SourceMatches()) return false;
            Transform[] current = skin.Renderer.bones;
            if (current.Length != skin.Bones.Length) return false;
            for (int i = 0; i < current.Length; i++) if (current[i] != skin.Bones[i]) return false;
        }
        return true;
    }

    internal bool TryCurrentTop(out float top)
    {
        top = float.NegativeInfinity;
        foreach (Skin skin in _skins) if (!skin.SourceMatches()) return false;
        _verifications++;
        foreach (Bone bone in _bones)
        {
            if (_root == null || bone.Transform == null || !bone.Transform.IsChildOf(_root)) return false;
            top = Mathf.Max(top, Top(bone.Transform.localToWorldMatrix, bone.Local));
        }
        return Finite(top);
    }

    internal bool TryTop(out float top)
    {
        top = float.NegativeInfinity;
        if (_root == null || _head == null || !_head.IsChildOf(_root)) return false;
        foreach (Skin skin in _skins) if (!skin.SourceMatches()) return false;
        // The body can animate its head as well as its wings. Keep the high-water mark in ANIMATOR
        // coordinates, not a head-relative offset or a lifetime/world-space maximum. Translation,
        // table zoom and a genuine new native animation must not inherit an earlier flight peak.
        if (_animator == null || !_animator.isActiveAndEnabled || _animator.layerCount == 0
            || _animator.runtimeAnimatorController != _preparedController
            || _animator.IsInTransition(0)) { _hasLoop = false; return TryCurrentTop(out top); }
        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        if (!state.loop) { _hasLoop = false; return TryCurrentTop(out top); }
        _stateSafe = StateIsQuiet(state.fullPathHash);
        _clips.Clear(); _animator.GetCurrentAnimatorClipInfo(0, _clips);
        if (_clips.Count == 0) { _hasLoop = false; return TryCurrentTop(out top); }
        int key = state.fullPathHash;
        bool haveBounds = false;
        Bounds cycle = default;
        foreach (AnimatorClipInfo clip in _clips)
        {
            if (clip.clip == null || clip.weight <= 0.001f) continue;
            if (_loops.TryGetValue(clip.clip, out Bounds bounds))
            {
                if (!haveBounds) { cycle = bounds; haveBounds = true; }
                else { cycle.Encapsulate(bounds.min); cycle.Encapsulate(bounds.max); }
            }
            key = unchecked(key * 31 + clip.clip.GetInstanceID());
        }
        Transform frame = _animator.transform;
        Vector3 scale = frame.lossyScale;
        Quaternion rotation = frame.rotation;
        bool changed = !_hasLoop || key != _loopState || scale != _loopScale || rotation != _loopRotation;
        if (!_hasLoop || key != _loopState || scale != _loopScale || rotation != _loopRotation)
        {
            _loopTop = float.NegativeInfinity; _loopState = key; _hasLoop = true;
            _loopScale = scale; _loopRotation = rotation;
            if (VRLog.WantsDebug && _cycleReports < 12)
            {
                _cycleReports++;
                VRLog.Info("WorldUI", $"BAR POSE native loop '{_root.name}' state={state.fullPathHash} "
                    + $"clips={_clips.Count} prepared={haveBounds} sparseEligible={SparseEligible} "
                    + $"interval={PerfConfig.ActorBarPoseCheckInterval:F3}s refusal='{_auditRefusal}'; cycle height fixed until state/scale/rotation changes "
                    + $"(bounded report {_cycleReports}/12).");
            }
        }
        float interval = PerfConfig.ActorBarPoseCheckInterval;
        bool sparse = interval > 0f && SparseEligible && haveBounds && _animator.layerCount == 1 && _clips.Count == 1;
        if (sparse && !changed && Time.unscaledTime < _nextVerification)
        {
            // Keep source/topology liveness immediate. Unknown bone writers, humanoid IK and
            // layered/blended animation are deliberately NOT admitted to the optional shortcut.
            foreach (Bone bone in _bones)
                if (bone.Transform == null || bone.Transform.parent != bone.Parent)
                { _quietRig = false; return TryCurrentTop(out top); }
            _skippedVerifications++;
        }
        else
        {
            if (!TryCurrentTop(out top)) return false;
            _verifiedRelativeTop = top - frame.position.y;
            _nextVerification = Time.unscaledTime + interval;
        }
        float relative = _verifiedRelativeTop;
        if (haveBounds) relative = Mathf.Max(relative, Top(frame.localToWorldMatrix, cycle) - frame.position.y);
        _loopTop = Mathf.Max(_loopTop, relative);
        top = frame.position.y + _loopTop;
        return true;
    }

    // SampleAnimation writes only private Transform copies through a disabled original Avatar
    // binding. No native state machine, renderer, script or gameplay callback runs on this
    // hierarchy. Transform copies alone do NOT bind the imported Generic Avatar clips.
    // Work is bounded and belongs to Capture (the existing loading/preparation path), not Tick.
    private void PrepareLoops()
    {
        Animator animator = _animator!;
        if (animator.runtimeAnimatorController == null) return;
        _preparedController = animator.runtimeAnimatorController;
        GameObject? host = null;
        try
        {
            var transforms = new Dictionary<Transform, Transform>();
            var needed = new HashSet<Transform> { _root };
            foreach (Bone bone in _bones)
                for (Transform? t = bone.Transform; t != null && t != _root; t = t.parent) needed.Add(t);
            for (Transform? t = animator.transform; t != null && t != _root; t = t.parent) needed.Add(t);
            _quietRig = !animator.isHuman;
            _hasNoStateBehaviours = animator.GetBehaviours<StateMachineBehaviour>().Length == 0;
            foreach (Transform transform in needed)
            {
                // Only the existing animation graph may deform the sampled skeleton. Unity IK,
                // constraints, native/mod procedural writers or unknown scripts retain exact
                // evaluated checks; no inference from a decorative object's name is made.
                foreach (Component component in transform.GetComponents<Component>())
                    if (component is MonoBehaviour || component is UnityEngine.Animations.IConstraint)
                    {
                        if (NativeActorPoseAudit.Allows(component, _root))
                        {
                            if (NativeActorPoseAudit.NeedsLiveCheck(component)) _liveAudit.Add(component);
                            continue;
                        }
                        _quietRig = false;
                        if (VRLog.WantsDebug && _auditRefusal.Length < 512)
                            _auditRefusal += (string.IsNullOrEmpty(_auditRefusal) ? "" : "; ")
                                + component.GetType().FullName + "@" + transform.name;
                    }
            }
            host = new GameObject("GloomhavenVR.BarLoopSampler") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            CloneTransforms(_root, host.transform, transforms, needed);
            Transform animated = transforms[animator.transform];
            // Native imported clips bind through their Avatar's skeleton map. The disabled
            // private Animator supplies that map; no state-machine behaviours are cloned.
            Animator sampler = animated.gameObject.AddComponent<Animator>();
            sampler.avatar = animator.avatar; sampler.enabled = false;
            var localBones = new Bone[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                localBones[i] = new Bone(transforms[_bones[i].Transform], _bones[i].Local);
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            int samplesLeft = 6144;
            foreach (AnimationClip clip in clips)
            {
                if (clip == null || !clip.isLooping || _loops.ContainsKey(clip) || _loops.Count >= 48) continue;
                if (clip.events.Length == 0) _eventFreeIdleLoops.Add(clip);
                int samples = Mathf.Clamp(Mathf.CeilToInt(clip.length * 90f), 32, 256);
                if (samples > samplesLeft) break;
                samplesLeft -= samples;
                foreach (KeyValuePair<Transform, Transform> pair in transforms)
                {
                    pair.Value.localPosition = pair.Key == _root ? Vector3.zero : pair.Key.localPosition;
                    pair.Value.localRotation = pair.Key == _root ? Quaternion.identity : pair.Key.localRotation;
                    pair.Value.localScale = pair.Key == _root ? Vector3.one : pair.Key.localScale;
                }
                bool initialized = false;
                Bounds cycle = default;
                for (int frame = 0; frame <= samples; frame++)
                {
                    clip.SampleAnimation(animated.gameObject, clip.length * frame / samples);
                    Matrix4x4 inverse = animated.worldToLocalMatrix;
                    foreach (Bone bone in localBones)
                    {
                        Matrix4x4 matrix = inverse * bone.Transform.localToWorldMatrix;
                        Bounds box = TransformBounds(matrix, bone.Local);
                        if (!initialized) { cycle = box; initialized = true; }
                        else { cycle.Encapsulate(box.min); cycle.Encapsulate(box.max); }
                    }
                }
                if (initialized && Finite(cycle.max.y)) _loops.Add(clip, cycle);
            }
        }
        catch (Exception error)
        {
            // Generic skins with unsupported clip bindings retain a native-state-scoped peak.
            // One bounded anomaly per preparation, not a stream tied to every animated frame.
            VRLog.Note("WorldUI", "BAR POSE cycle preparation unavailable (" + error.GetType().Name
                + "); evaluated native-state peak retained.");
        }
        finally { if (host != null) UnityEngine.Object.Destroy(host); }
    }

    private bool StateIsQuiet(int fullPathHash)
    {
        if (_stateAudits.TryGetValue(fullPathHash, out bool safe)) return safe;
        // Auditing the entire controller rejects every actor because attack/death/timeline
        // behaviours coexist with its harmless idle state. Only the CURRENT native state
        // may use the shortcut. Unknown state callbacks keep evaluated checks immediately.
        StateMachineBehaviour[] behaviours = _animator!.GetBehaviours(fullPathHash, 0);
        safe = behaviours != null || _hasNoStateBehaviours;
        if (behaviours != null)
            foreach (StateMachineBehaviour behaviour in behaviours)
                if (behaviour == null || !NativeActorPoseAudit.AllowsStateBehaviour(behaviour))
                { safe = false; break; }
        if (_stateAudits.Count < 128) _stateAudits.Add(fullPathHash, safe);
        return safe;
    }

    private static Transform CloneTransforms(Transform source, Transform parent,
        Dictionary<Transform, Transform> copies, HashSet<Transform> needed)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false); copies[source] = copy;
        copy.localPosition = copies.Count == 1 ? Vector3.zero : source.localPosition;
        copy.localRotation = copies.Count == 1 ? Quaternion.identity : source.localRotation;
        copy.localScale = copies.Count == 1 ? Vector3.one : source.localScale;
        foreach (Transform child in source) if (needed.Contains(child)) CloneTransforms(child, copy, copies, needed);
        return copy;
    }

    private static Bounds TransformBounds(in Matrix4x4 matrix, in Bounds box)
    {
        Vector3 e = box.extents;
        Vector3 extents = new(Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
            Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
            Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
        return new Bounds(matrix.MultiplyPoint3x4(box.center), extents * 2f);
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
        // The original figure-follow health/status band reaches ~0.23wu below its anchor. The
        // old 0.05 minimum could put that LOWER EDGE inside the evaluated native waking pose
        // even while the anchor point itself cleared the skin. Reserve that band plus 0.03wu.
        float needed = top - trackY + Mathf.Max(0.26f, 0.12f * height);
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

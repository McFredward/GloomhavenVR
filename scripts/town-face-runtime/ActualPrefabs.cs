using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;

/// <summary>Optional final-asset integration: execute the production binder and motion against
/// the imported prefabs. Bounds prove finite geometry, not facial beauty or absence of seams.</summary>
internal static class ActualPrefabs
{
    [Serializable] private sealed class Record
    {
        public string npc = "", asset = "";
        public int faceLods, shapeBindings, assertions;
        public float eyeSeparationMetres, opticalForwardDot, maximumEyeTargetErrorDegrees;
        public Vector3 actorBoundsMinimum, actorBoundsMaximum;
    }
    [Serializable] private sealed class Evidence
    { public string bundle = "", unity = ""; public Record[] residents = Array.Empty<Record>(); public int assertions; }
    private static int _count;
    private static void Check([DoesNotReturnIf(false)] bool value, string message)
    { _count++; if (!value) throw new Exception("Actual NPC prefab: " + message); }
    private static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x)
        && !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
    private static void SampleBody(Animation animation, string clip, float seconds)
    {
        animation.Stop();
        AnimationState state = animation[clip];
        Check(state != null, "original body clip exists: " + clip);
        state!.enabled = true; state.weight = 1f; state.time = seconds;
        animation.Sample(); state.enabled = false;
    }
    private static Transform Named(Transform root, string name)
    {
        Transform[] candidates = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
        Check(candidates.Length == 1, "exactly one " + name + " transform");
        return candidates[0];
    }
    private static Bounds SkinBounds(SkinnedMeshRenderer renderer, Transform reference)
    {
        // Explicit CPU skinning avoids FBX renderer scale100 / BakeMesh useScale ambiguities.
        // Apply the actual blendshape frame deltas before bone matrices, as Unity does.
        Mesh mesh = renderer.sharedMesh;
        Vector3[] vertices = mesh.vertices, delta = new Vector3[mesh.vertexCount];
        for (int shape = 0; shape < mesh.blendShapeCount; shape++)
        {
            float weight = renderer.GetBlendShapeWeight(shape);
            if (Mathf.Abs(weight) < .0001f) continue;
            int last = mesh.GetBlendShapeFrameCount(shape) - 1;
            mesh.GetBlendShapeFrameVertices(shape, last, delta, null, null);
            float amount = weight / mesh.GetBlendShapeFrameWeight(shape, last);
            for (int n = 0; n < vertices.Length; n++) vertices[n] += delta[n] * amount;
        }
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bind = mesh.bindposes;
        Transform[] bones = renderer.bones;
        Matrix4x4[] matrices = bones.Select((bone, n) => reference.worldToLocalMatrix * bone.localToWorldMatrix * bind[n]).ToArray();
        var bounds = new Bounds();
        bool first = true;
        for (int n = 0; n < vertices.Length; n++)
        {
            BoneWeight w = weights[n]; Vector3 v = vertices[n];
            Vector3 point = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            if (!Finite(point)) throw new Exception("Actual NPC prefab: nonfinite skinned vertex");
            if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
        }
        Check(!first, "actual skinned mesh contains finite vertices");
        return bounds;
    }
    internal static int Run(string path, string evidencePath)
    {
        _count = 0;
        AssetBundle bundle = AssetBundle.LoadFromFile(path);
        Check(bundle != null, "Linux validation bundle loads");
        var records = new List<Record>();
        try
        {
            foreach (string npc in new[] { "merchant", "priestess", "enchantress" })
            {
                int begin = _count;
                string[] paths = bundle!.GetAllAssetNames().Where(a => a.EndsWith("/town" + npc + ".prefab", StringComparison.OrdinalIgnoreCase)).ToArray();
                Check(paths.Length == 1, npc + " prefab is unambiguous");
                GameObject prefab = bundle.LoadAsset<GameObject>(paths[0]);
                Check(prefab != null, npc + " prefab loads");
                GameObject instance = UnityEngine.Object.Instantiate(prefab!)!;
                try
                {
                    Transform root = instance.transform;
                    root.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(0, 37, 0));
                    root.localScale = Vector3.one * .8f;
                    Transform actor = root.Find("Actor"), head = Named(root, "Head"), left = Named(root, "EyeLeft"), right = Named(root, "EyeRight");
                    Check(actor != null && left.IsChildOf(head) && right.IsChildOf(head), npc + " eyes share original Head hierarchy");
                    Animation animation = root.GetComponentInChildren<Animation>(true) ?? throw new Exception(npc + " body Animation missing");
                    Check(animation != null, npc + " retains native authored body animation");
                    SampleBody(animation!, "Idle", 0);
                    var rig = new TownServiceFaceRig(root);
                    Check(rig.Ready && rig.Complete, npc + " production binder accepts final imported contract");
                    float opticalDot = Vector3.Dot(rig.OpticalRotation * Vector3.forward, -root.forward);
                    Check(opticalDot > .98f, npc + " neutral optical forward faces station -Z");
                    Check(Vector3.Dot(left.forward, right.forward) > .9999f, npc + " rest optical axes parallel");
                    float separation = Vector3.Distance(left.position, right.position) / root.lossyScale.x;
                    Check(separation >= .035f && separation <= .085f, npc + " eye separation is anatomical metres");
                    Check(Vector3.Distance(head.position, rig.EyePosition) / root.lossyScale.x < .35f, npc + " eyes attach near head pivot");
                    var faces = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Where(r => r.sharedMesh != null && r.sharedMesh.GetBlendShapeIndex("BlinkLeft") >= 0).ToArray();
                    Check(faces.Length == 3, npc + " three imported facial LOD renderers");
                    int bindings = 0;
                    foreach (SkinnedMeshRenderer renderer in faces)
                        foreach (string shape in TownServiceFaceRig.ShapeNames)
                        { Check(renderer.sharedMesh.GetBlendShapeIndex(shape) >= 0, npc + " " + renderer.name + " binds " + shape); bindings++; }
                    LODGroup lod = actor!.GetComponent<LODGroup>() ?? throw new Exception(npc + " LOD group missing");
                    Check(lod != null && lod.GetLODs().Length == 3, npc + " retains three body LOD levels");
                    foreach (LOD level in lod!.GetLODs())
                        Check(level.renderers.OfType<SkinnedMeshRenderer>().Any(r => faces.Contains(r)), npc + " each visible LOD includes its facial renderer");
                    float maximumError = 0;
                    Bounds envelope = default; bool envelopeStarted = false;
                    foreach (string clip in new[] { "Idle", "Greeting", "Gesture", "ReturnToIdle" })
                    {
                        for (int step = 0; step < 3; step++)
                        {
                            float time = animation[clip].length * step * .45f;
                            rig.BeforeBodySample(); SampleBody(animation!, clip, time);
                            Quaternion basis = rig.OpticalRotation, bodyHead = head.localRotation;
                            Vector3 target = rig.EyePosition + basis * new Vector3(0, 0, 1.2f * root.lossyScale.x);
                            TownFacePose aim = default;
                            for (int frame = 0; frame < 120; frame++)
                                aim = TownServiceFaceMotion.Aim(basis, root.lossyScale.x, rig.HeadPosition,
                                    rig.LeftPosition, rig.RightPosition, target, in aim, 1f / 90f);
                            Check(Mathf.Abs(aim.HeadYaw) < .05f && Mathf.Abs(aim.HeadPitch) < .05f,
                                npc + " sampled native head axes retain neutral optical target");
                            var pose = TownServiceFaceMotion.Evaluate(in aim, .3f, 1, Vector3.zero);
                            rig.Apply(in pose);
                            float error = Mathf.Max(Vector3.Angle(left.forward, target - left.position), Vector3.Angle(right.forward, target - right.position));
                            maximumError = Mathf.Max(maximumError, error);
                            Check(error < .1f, npc + " actual eyeballs converge toward neutral target");
                            rig.BeforeBodySample();
                            Check(Quaternion.Angle(head.localRotation, bodyHead) < .04f, npc + " reset restores sampled imported Head before body clip");
                            Vector3 sideTarget = rig.EyePosition + basis * new Vector3(.4f, .2f, 1.2f) * root.lossyScale.x;
                            TownFacePose sideAim = default;
                            for (int frame = 0; frame < 120; frame++)
                                sideAim = TownServiceFaceMotion.Aim(basis, root.lossyScale.x, rig.HeadPosition,
                                    rig.LeftPosition, rig.RightPosition, sideTarget, in sideAim, 1f / 90f);
                            var sidePose = TownServiceFaceMotion.Evaluate(in sideAim, .3f, 1, Vector3.zero);
                            rig.Apply(in sidePose);
                            float sideError = Mathf.Max(Vector3.Angle(left.forward, sideTarget - left.position), Vector3.Angle(right.forward, sideTarget - right.position));
                            maximumError = Mathf.Max(maximumError, sideError);
                            Check(sideError < .15f, npc + " actual moving head and eyes converge on elevated side target");
                            rig.BeforeBodySample();
                            Check(Quaternion.Angle(head.localRotation, bodyHead) < .04f, npc + " reset restores sampled imported Head before body clip");
                            pose = new TownServiceFacePose { HeadYaw = 35, HeadPitch = 12, LeftYaw = -10, RightYaw = -12,
                                LeftPitch = -5, RightPitch = -5, BlinkLeft = .4f, BlinkRight = .6f, JawOpen = .2f,
                                MouthWide = .15f, MouthRound = .1f, Smile = .08f, BrowRaise = .04f };
                            Quaternion expectedHead = basis * Quaternion.Euler(pose.HeadPitch, pose.HeadYaw, pose.HeadRoll)
                                * Quaternion.Inverse(basis) * head.rotation;
                            rig.Apply(in pose);
                            Check(Quaternion.Angle(head.rotation, expectedHead) < .04f, npc + " imported Head uses optical-frame rotation");
                            foreach (SkinnedMeshRenderer renderer in faces)
                                for (int shape = 0; shape < TownServiceFaceRig.ShapeNames.Length; shape++)
                                {
                                    int index = renderer.sharedMesh.GetBlendShapeIndex(TownServiceFaceRig.ShapeNames[shape]);
                                    Check(Mathf.Abs(renderer.GetBlendShapeWeight(index) - pose.Weight(shape) * 100f) < .001f,
                                        npc + " runtime weight reaches " + renderer.name + "/" + TownServiceFaceRig.ShapeNames[shape]);
                                }
                            Quaternion eyeBefore = left.localRotation;
                            SampleBody(animation!, clip, time);
                            Check(Quaternion.Angle(eyeBefore, left.localRotation) < .04f, npc + " native body clip never overwrites gaze eye pivot");
                            foreach (SkinnedMeshRenderer renderer in faces)
                                Check(Mathf.Abs(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("BlinkLeft")) - 40) < .001f,
                                    npc + " native body clip never overwrites cached facial weight");
                            rig.BeforeBodySample(); SampleBody(animation!, clip, time);
                            rig.Apply(in pose);
                            var bodyParts = lod.GetLODs()[0].renderers.OfType<SkinnedMeshRenderer>().ToArray();
                            Check(bodyParts.Length > 0, npc + " first body LOD contains skinned geometry");
                            Bounds sample = SkinBounds(bodyParts[0], root);
                            foreach (SkinnedMeshRenderer part in bodyParts.Skip(1))
                            { Bounds partBounds = SkinBounds(part, root); sample.Encapsulate(partBounds.min); sample.Encapsulate(partBounds.max); }
                            if (!envelopeStarted) { envelope = sample; envelopeStarted = true; }
                            else { envelope.Encapsulate(sample.min); envelope.Encapsulate(sample.max); }
                            Check(sample.size.y > 1.4f && sample.size.y < 2.2f, npc + " animated skinned height remains human scale");
                        }
                    }
                    rig.BeforeBodySample(); SampleBody(animation!, "Idle", 0);
                    Quaternion original = head.localRotation;
                    var repeated = new TownServiceFacePose { HeadYaw = -40, HeadPitch = -15 };
                    for (int frame = 0; frame < 120; frame++)
                    { rig.Apply(in repeated); rig.BeforeBodySample(); }
                    Check(Quaternion.Angle(original, head.localRotation) < .04f, npc + " repeated gaze does not accumulate over native head pose");
                    Check(envelope.min.y > -.1f && envelope.max.y < 2.2f && envelope.size.x < 2f && envelope.size.z < 2f,
                        npc + " animated neck/head bounds remain in actor envelope");
                    records.Add(new Record { npc = npc, asset = paths[0], faceLods = faces.Length, shapeBindings = bindings,
                        eyeSeparationMetres = separation, opticalForwardDot = opticalDot, maximumEyeTargetErrorDegrees = maximumError,
                        actorBoundsMinimum = envelope.min, actorBoundsMaximum = envelope.max, assertions = _count - begin });
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            File.WriteAllText(evidencePath, JsonUtility.ToJson(new Evidence { bundle = path, unity = Application.unityVersion,
                residents = records.ToArray(), assertions = _count }, true) + "\n");
        }
        finally { if (bundle != null) bundle.Unload(true); }
        return _count;
    }
}

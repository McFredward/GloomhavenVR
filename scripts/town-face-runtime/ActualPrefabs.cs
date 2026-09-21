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
        public int faceLods, shapeBindings, assertions, skullProbes, jawProbes, rejectedJawWeightCorruptions;
        public float maximumSkullRigidityErrorMetres;
        public float eyeSeparationMetres, opticalForwardDot, maximumEyeTargetErrorDegrees;
        public Vector3 actorBoundsMinimum, actorBoundsMaximum;
    }
    [Serializable] private sealed class Evidence
    { public string bundle = "", unity = ""; public Record[] residents = Array.Empty<Record>(); public int assertions; }
    // Probe IDs originate in official template anatomy, independently of the final skin weights.
    [Serializable] private sealed class AnatomyContract
    { public int version = 0; public ResidentAnatomy[] residents = Array.Empty<ResidentAnatomy>(); }
    [Serializable] private sealed class ResidentAnatomy
    { public string npc = ""; public LodAnatomy[] lods = Array.Empty<LodAnatomy>(); }
    [Serializable] private sealed class LodAnatomy
    { public string renderer = ""; public int vertexCount = 0; public int[] skull = Array.Empty<int>(), jaw = Array.Empty<int>(); }
    private struct AnatomyResult
    { internal int Skull, Jaw, Rejected; internal float MaximumError; }
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
    private static Vector3[] SkinVertices(SkinnedMeshRenderer renderer, Transform reference, BoneWeight[]? overrideWeights = null)
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
        BoneWeight[] weights = overrideWeights ?? mesh.boneWeights;
        Matrix4x4[] bind = mesh.bindposes;
        Transform[] bones = renderer.bones;
        Matrix4x4[] matrices = bones.Select((bone, n) => reference.worldToLocalMatrix * bone.localToWorldMatrix * bind[n]).ToArray();
        for (int n = 0; n < vertices.Length; n++)
        {
            BoneWeight w = weights[n]; Vector3 v = vertices[n];
            Vector3 point = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            if (!Finite(point)) throw new Exception("Actual NPC prefab: nonfinite skinned vertex");
            vertices[n] = point;
        }
        return vertices;
    }
    private static Bounds SkinBounds(SkinnedMeshRenderer renderer, Transform reference)
    {
        Vector3[] vertices = SkinVertices(renderer, reference);
        Check(vertices.Length > 0, "actual skinned mesh contains finite vertices");
        var bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 point in vertices) bounds.Encapsulate(point);
        return bounds;
    }
    private static float HeadWeight(BoneWeight w, int head) =>
        (w.boneIndex0 == head ? w.weight0 : 0) + (w.boneIndex1 == head ? w.weight1 : 0)
        + (w.boneIndex2 == head ? w.weight2 : 0) + (w.boneIndex3 == head ? w.weight3 : 0);
    private static AnatomyResult CheckAnatomy(string npc, ResidentAnatomy contract, Transform root,
        Animation animation, TownServiceFaceRig rig, SkinnedMeshRenderer[] faces)
    {
        Check(contract.lods.Length == faces.Length, npc + " independent anatomical probes cover every facial LOD");
        var result = new AnatomyResult();
        foreach (SkinnedMeshRenderer renderer in faces)
        {
            LodAnatomy[] matches = contract.lods.Where(l => l.renderer == renderer.name).ToArray();
            Check(matches.Length == 1, npc + " unique anatomical probe renderer " + renderer.name);
            LodAnatomy probes = matches[0]; Mesh mesh = renderer.sharedMesh;
            Check(probes.vertexCount == mesh.vertexCount, npc + " anatomical probe indices match final imported mesh");
            Check(probes.skull.Distinct().Count() >= 6 && probes.jaw.Distinct().Count() >= 6,
                npc + " independent skull and lower-jaw probes are populated");
            int[] indices = probes.skull.Concat(probes.jaw).Distinct().ToArray();
            Check(indices.All(i => i >= 0 && i < mesh.vertexCount), npc + " anatomical probe indices are in range");
            int head = Array.IndexOf(renderer.bones, rig.Head);
            int neck = Array.FindIndex(renderer.bones, b => b.name == "Neck");
            Check(head >= 0 && neck >= 0, npc + " anatomical head and neck bones exist in imported skin");
            BoneWeight[] weights = mesh.boneWeights;
            foreach (int index in indices)
                Check(HeadWeight(weights[index], head) >= .999f,
                    npc + " semantic skull/jaw vertex stays rigid with Head: " + renderer.name + "/" + index);
            result.Skull += probes.skull.Distinct().Count(); result.Jaw += probes.jaw.Distinct().Count();
            bool rejected = false;
            // Keep expression fixed between baseline and rotated sample: legitimate jaw opening
            // remains allowed, while body skinning must not drag the posed jaw toward the neck.
            foreach (float jawOpen in new[] { 0f, .65f })
            {
                rig.BeforeBodySample(); SampleBody(animation, "Idle", 0);
                var baselinePose = new TownServiceFacePose { JawOpen = jawOpen, Smile = jawOpen * .3f };
                rig.Apply(in baselinePose);
                Vector3[] neutral = SkinVertices(renderer, root);
                Matrix4x4 neutralToHead = rig.Head.worldToLocalMatrix * root.localToWorldMatrix;
                Vector3 pivot = root.InverseTransformPoint(rig.Head.position);
                int jawProbe = probes.jaw.OrderByDescending(i => (neutral[i] - pivot).sqrMagnitude).First();
                BoneWeight[] broken = (BoneWeight[])weights.Clone();
                broken[jawProbe] = new BoneWeight { boneIndex0 = head, weight0 = .5f, boneIndex1 = neck, weight1 = .5f };
                Vector3 brokenNeutral = SkinVertices(renderer, root, broken)[jawProbe];
                foreach (float yaw in new[] { -50f, 50f })
                    foreach (float pitch in new[] { -22f, 22f })
                    {
                        rig.BeforeBodySample(); SampleBody(animation, "Idle", 0);
                        var pose = baselinePose; pose.HeadYaw = yaw; pose.HeadPitch = pitch;
                        rig.Apply(in pose);
                        Matrix4x4 expected = root.worldToLocalMatrix * rig.Head.localToWorldMatrix * neutralToHead;
                        Vector3[] actual = SkinVertices(renderer, root);
                        foreach (int index in indices)
                        {
                            float error = Vector3.Distance(actual[index], expected.MultiplyPoint3x4(neutral[index]));
                            result.MaximumError = Mathf.Max(result.MaximumError, error);
                            Check(error < .0005f, npc + " skull/jaw shape remains rigid within 0.5 mm at gaze limits: " + renderer.name + "/" + index);
                        }
                        float corruptedError = Vector3.Distance(SkinVertices(renderer, root, broken)[jawProbe], expected.MultiplyPoint3x4(brokenNeutral));
                        rejected |= corruptedError >= .0005f;
                    }
            }
            Check(rejected, npc + " anatomical gate detects deliberately mixed Head/Neck jaw skinning");
            result.Rejected++;
        }
        rig.BeforeBodySample(); SampleBody(animation, "Idle", 0);
        var clear = new TownServiceFacePose(); rig.Apply(in clear); rig.BeforeBodySample();
        return result;
    }
    internal static int Run(string path, string evidencePath)
    {
        _count = 0;
        AssetBundle bundle = AssetBundle.LoadFromFile(path);
        Check(bundle != null, "Linux validation bundle loads");
        var records = new List<Record>();
        try
        {
            string[] contracts = bundle!.GetAllAssetNames().Where(a => a.EndsWith("/town-facial-rig-contract.json", StringComparison.OrdinalIgnoreCase)).ToArray();
            Check(contracts.Length == 1, "bundle includes independent anatomical probe contract");
            TextAsset contractAsset = bundle.LoadAsset<TextAsset>(contracts[0]);
            Check(contractAsset != null, "anatomical probe contract loads");
            AnatomyContract anatomy = JsonUtility.FromJson<AnatomyContract>(contractAsset!.text);
            Check(anatomy != null && anatomy.version == 1, "supported anatomical probe contract version");
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
                    ResidentAnatomy[] residentContracts = anatomy!.residents.Where(r => r.npc == npc).ToArray();
                    Check(residentContracts.Length == 1, npc + " independent anatomical contract is unambiguous");
                    AnatomyResult anatomical = CheckAnatomy(npc, residentContracts[0], root, animation, rig, faces);
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
                        actorBoundsMinimum = envelope.min, actorBoundsMaximum = envelope.max, assertions = _count - begin,
                        skullProbes = anatomical.Skull, jawProbes = anatomical.Jaw, rejectedJawWeightCorruptions = anatomical.Rejected,
                        maximumSkullRigidityErrorMetres = anatomical.MaximumError });
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

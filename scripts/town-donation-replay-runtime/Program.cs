using System;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool okay, string text)
    { _checks++; if (!okay) throw new Exception(text); }
    private static AudioSource? SpeechSource()
    {
        Array entries = (Array)typeof(TownServiceVoice).GetField("Playbacks", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object entry = entries.GetValue(1)!;
        return (AudioSource?)entry.GetType().GetField("Source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry);
    }
    private static T NativeAsset<T>(string name) where T : UnityEngine.Object
    {
        // Invoke the Editor's actual importer without referencing both its
        // monolithic facade and engine module in this independent runtime DLL.
        Type database = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.AssetDatabase"))
            .First(t => t != null)!;
        MethodInfo load = database.GetMethod("LoadAssetAtPath", new[] {typeof(string), typeof(Type)})!;
        return (T)load.Invoke(null, new object[] {"Assets/Speech/" + name, typeof(T)})!;
    }
    private static void RefreshVoice()
    {
        typeof(TownServiceVoice).GetField("_frame", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, -1);
        typeof(TownServiceVoice).GetField("_nextContext", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0f);
    }
    private static (GameObject Root, Mesh Skin, SkinnedMeshRenderer Renderer, Transform Head) Face(string name)
    {
        var root = new GameObject(name);
        var head = new GameObject("Head").transform;
        head.SetParent(root.transform, false); head.localPosition = new Vector3(0f, 1.6f, 0f);
        foreach (int side in new[] {-1, 1})
        {
            var eye = new GameObject(side < 0 ? "EyeLeft" : "EyeRight").transform;
            eye.SetParent(head, false); eye.localPosition = new Vector3(side * .025f, 0f, .04f);
        }
        // Deliberately explicit mathematical anatomy: actual Unity skin/bones and
        // production Face/Rig run here. This does not certify final NPC likeness.
        var mesh = new Mesh();
        mesh.vertices = new[] {new Vector3(-.04f, 1.6f, 0f), new Vector3(.04f, 1.6f, 0f), new Vector3(0f, 1.65f, 0f)};
        mesh.triangles = new[] {0, 1, 2};
        mesh.bindposes = new[] {head.worldToLocalMatrix * root.transform.localToWorldMatrix};
        mesh.boneWeights = Enumerable.Repeat(new BoneWeight {boneIndex0 = 0, weight0 = 1f}, 3).ToArray();
        foreach (string shape in TownServiceFaceRig.ShapeNames)
            mesh.AddBlendShapeFrame(shape, 100f, new[] {Vector3.zero, Vector3.zero, Vector3.up * .001f}, new Vector3[3], new Vector3[3]);
        var skin = root.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh; skin.bones = new[] {head}; skin.rootBone = head;
        return (root, mesh, skin, head);
    }
    private static TownActivityVisual Body(in TownActivityState activity)
    {
        var result = TownServiceActivityMotion.Visual(2, activity.Temple);
        TownServiceActivityMotion.ApplyTempleAvailability(ref result, activity.TempleUnavailableBlend <= 0f, activity.TempleUnavailableBlend);
        if (activity.Interactive)
            TownServiceActivityMotion.ApplyTempleBlessing(ref result, activity.Clock - activity.TempleBlessingStartedClock);
        TownServiceActivityMotion.ApplyTempleBreath(ref result, activity.Clock);
        return result;
    }
    public static int Run()
    {
        _checks = 0;
        for (int n = 0; n < 5; n++)
        {
            string name = "priestess-donate" + (n == 0 ? "" : "-" + (n + 1));
            AudioClip clip = NativeAsset<AudioClip>(name + ".wav");
            TextAsset curve = NativeAsset<TextAsset>(name + ".json");
            Check(clip != null && curve != null, "original shipped donation voice and curve import through Unity");
            clip!.LoadAudioData(); TownServiceAssets.Clips[name] = clip; TownServiceAssets.Curves[name] = curve!;
        }
        TownServiceAssets.Clips["spell-soft-4"] = NativeAsset<AudioClip>("spell-soft-4.wav");
        TownServiceAssets.Clips["spell-soft-4"].LoadAudioData();
        var owner = Face("Donation author");
        var observer = Face("Late donation observer");
        var listener = new GameObject("Local head");
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(template.GetComponent<Collider>());
        var camera = listener.AddComponent<Camera>(); camera.enabled = false;
        VRRigDriver.HeadCamera = camera;
        listener.transform.position = new Vector3(-.05f, 1.6f, .6f);
        NetAvatarDriver.Heads[7] = new Vector3(.75f, 1.6f, 1.2f);
        NetAvatarDriver.Heads[9] = new Vector3(.05f, 1.6f, .6f);
        TownServicePopulation.Frame = owner.Root.transform;
        TownServicePopulation.IsFaceAuthor = true;
        TownServiceDecor.MoneyBagTemplate = template.transform;
        ReplayClock.Now = 100f; ReplayClock.Delta = .02f;
        try
        {
            var gate = new NativeDonationGate.TempleBlessingGate();
            Check(gate.Observe(true, 7, 17, true, false, 1, 0f, true),
                "fresh explicit native donor revision starts one cosmetic blessing");
            TownServiceFaceAttention.BlessVisitor(7, 0f);
            var attention = new TownServiceFaceAttention();
            Vector3? focus = attention.Select(2, owner.Root.transform, Quaternion.identity, owner.Head.position);
            Check(focus.HasValue && Vector3.Distance(focus.Value, NetAvatarDriver.Heads[7]) < .0001f,
                "committed donor wins priestess attention over a closer visitor");
            TownServiceVoice.RequestReaction(2, TownVoiceReaction.PriestessDonate);
            using var a = new TownServiceTempleBowlMarker(owner.Root.transform, true);
            using var b = new TownServiceTempleBowlMarker(observer.Root.transform, true);
            using var ownerAudio = new TownServiceActivityAudio(owner.Root.transform, 2);
            using var observerAudio = new TownServiceActivityAudio(observer.Root.transform, 2);
            var authorStation = new StationBlessingDispatch(a, ownerAudio);
            var followerStation = new StationBlessingDispatch(b, observerAudio);
            a.Tick(false); b.Tick(false);
            var authorFace = new TownServiceFace(owner.Root.transform, 2);
            var observerFace = new TownServiceFace(observer.Root.transform, 2);
            var activity = new TownActivityState {Active = true, Epoch = 81, Sequence = 1, Clock = 0f,
                HasSharedPerformance = true, Interactive = true, TempleBlessingGeneration = 1,
                Temple = new TownActivityPose {Engaged = true, TransitionAge = TownActivityPose.TransitionSeconds, WorkClock = 5f}};
            TownFacePose shown = default;
            for (int n = 0; n <= 30; n++)
            {
                float age = n * .02f; ReplayClock.Now = 100f + age;
                activity.Clock = age;
                authorStation.SampleTempleBlessing(activity.Epoch, activity.TempleBlessingGeneration, age, true);
                a.Tick(false);
                authorFace.BeforeBodySample(); authorFace.PrepareActivityAttention(true);
                shown = authorFace.Tick(true, false, 1, default, 0f, age, age, activity.Epoch);
            }
            Check(shown.Cue >= 36 && shown.Cue <= 40 && shown.Generation == 1 && Math.Abs(shown.SpeechAge - .6f) < .001f,
                "native donation gratitude has one exact author cue and event age");
            Check(owner.Head.localRotation != Quaternion.identity, "production author face applies donor-directed bone motion");
            Quaternion ownerHead = owner.Head.localRotation;
            float[] ownerMouth = Enumerable.Range(0, owner.Skin.blendShapeCount).Select(owner.Renderer.GetBlendShapeWeight).ToArray();
            var faces = new TownFaceState {Active = true, Epoch = activity.Epoch, Sequence = activity.Sequence,
                Clock = activity.Clock, Temple = shown};
            var packet = new byte[TownActivityCodec.PacketBytes];
            int length = TownActivityCodec.WritePacket(packet, activity, faces);
            Check(TownActivityCodec.ReadPacket(packet, length, out var receivedActivity, out var receivedFaces) && length > 0,
                "complete original donation body and face codecs roundtrip the real performance");
            Check(!TownActivityCodec.ReadPacket(packet, length - 1, out _, out _), "truncated paired donation packet cannot publish half a performance");
            ReplayClock.Now = 200f;
            RemoteTownActivities.Reset(); RemoteTownFaces.Reset();
            Check(RemoteTownPerformance.Observe(1, receivedActivity, receivedFaces, true),
                "original atomic receiver accepts the matching donation presence");
            bool sampledActivity = RemoteTownActivities.Sample(1, out var playback, out _);
            Check(RemoteTownFaces.Sample(1, out var facial, out float elapsed) && sampledActivity,
                "original observer interpolation supplies both halves of the donation");
            Check(playback.TempleBlessingGeneration == 1 && playback.Interactive,
                "received paired packet retains the committed blessing");
            float replayAge = playback.Clock - playback.TempleBlessingStartedClock;
            Check(Math.Abs(replayAge - .6f) < .0001f, "receiver keeps the author donation age after a different local clock");
            var ownerBody = Body(activity); var observerBody = Body(playback);
            Check(Vector3.Distance(ownerBody.Left, observerBody.Left) < .0001f
                && Vector3.Distance(ownerBody.Right, observerBody.Right) < .0001f,
                "received blessing uses identical original body target mathematics");
            TownServiceVoice.Reset(); RefreshVoice(); TownServicePopulation.IsFaceAuthor = false;
            followerStation.SampleTempleBlessing(playback.Epoch, playback.TempleBlessingGeneration, replayAge, playback.Interactive);
            b.Tick(false);
            followerStation.SampleActivityAudio(1, playback.Epoch, playback.Temple.WorkClock,
                true, observerBody, true, false, playback.Clock, default);
            observerFace.BeforeBodySample();
            observerFace.Tick(false, true, 1, facial.Temple, elapsed, playback.Clock, replayAge, playback.Epoch);
            Check(Quaternion.Angle(ownerHead, observer.Head.localRotation) < .02f,
                "received donor gaze reaches real Unity head bones without local target election");
            for (int n = 0; n < ownerMouth.Length; n++)
                Check(Math.Abs(ownerMouth[n] - observer.Renderer.GetBlendShapeWeight(n)) < (n >= 7 ? .08f : .01f),
                    "received exact voice curve reaches the same real Unity facial blend shapes: "
                    + TownServiceFaceRig.ShapeNames[n] + " " + ownerMouth[n] + " vs " + observer.Renderer.GetBlendShapeWeight(n));
            var foley = observer.Root.GetComponentsInChildren<AudioSource>().FirstOrDefault(source => source.clip == TownServiceAssets.Clips["spell-soft-4"]);
            Check(foley != null && foley.isPlaying && Math.Abs(foley.time - replayAge) < .025f,
                "received donation Foley starts at the same shared age");
            AudioSource? speech = SpeechSource();
            Check(speech != null && speech.isPlaying && speech.clip != null
                && speech.clip.name.StartsWith("priestess-donate", StringComparison.Ordinal)
                && Math.Abs(speech.time - replayAge) < .025f,
                "received donation voice starts the original imported take at the shared age");
            ParticleSystem[] aa = a.Root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem[] bb = b.Root.GetComponentsInChildren<ParticleSystem>(true);
            Check(aa.Length == 4 && bb.Length == 4, "received donation creates all four original seeded particle phases");
            int total = 0;
            for (int n = 0; n < aa.Length; n++)
            {
                Check(!aa[n].isPlaying && !bb[n].isPlaying, "packet replay particles stay on the shared event clock");
                var ap = new ParticleSystem.Particle[aa[n].main.maxParticles];
                var bp = new ParticleSystem.Particle[bb[n].main.maxParticles];
                int ac = aa[n].GetParticles(ap), bc = bb[n].GetParticles(bp); total += bc;
                Check(ac == bc, "received donation retains the exact owner particle count");
                Check(bb[n].gameObject.activeInHierarchy && bb[n].GetComponent<ParticleSystemRenderer>().enabled,
                    "received blessing particles remain enabled in the actual Unity hierarchy");
                for (int k = 0; k < ac; k++)
                    Check(ap[k].randomSeed == bp[k].randomSeed && Vector3.Distance(ap[k].position, bp[k].position) < .003f,
                        "received donation keeps exact seeded particle identity and geometry");
            }
            Light ownerLight = a.Root.GetComponentInChildren<Light>(true);
            Light observerLight = b.Root.GetComponentInChildren<Light>(true);
            Check(ownerLight != null && observerLight != null && ownerLight.enabled && observerLight.enabled
                && Math.Abs(ownerLight.intensity - observerLight.intensity) < .0001f,
                "received donation also drives the same real shared-age blessing light");
            Check(total > 0, "actual received donation has visible particle content");
            Check(!gate.Observe(true, 7, 17, true, false, 1, .6f, true), "duplicate donor revision cannot queue another gratitude or blessing");
            speech!.time = .8f;
            observerFace.BeforeBodySample();
            observerFace.Tick(false, true, 1, facial.Temple, .1f, playback.Clock + .1f, replayAge + .1f, playback.Epoch);
            Check(Math.Abs(speech.time - .8f) < .025f, "duplicate paired generation cannot rewind the real shared speech source");
            followerStation.SampleTempleBlessing(playback.Epoch, playback.TempleBlessingGeneration,
                TownServiceActivityMotion.TempleBlessingVisualSeconds, true);
            Check(bb.All(system => system.particleCount == 0), "received donation ends every original particle phase at its bounded shared deadline");
        }
        finally
        {
            TownServiceVoice.Reset(); TownServiceFaceAttention.ResetBlessingFocus();
            RemoteTownActivities.Reset(); RemoteTownFaces.Reset();
            NetAvatarDriver.Heads.Clear(); VRRigDriver.HeadCamera = null;
            TownServiceDecor.MoneyBagTemplate = null;
            TownServiceAssets.Clips.Clear(); TownServiceAssets.Curves.Clear();
            UnityEngine.Object.DestroyImmediate(owner.Root); UnityEngine.Object.DestroyImmediate(observer.Root);
            UnityEngine.Object.DestroyImmediate(owner.Skin); UnityEngine.Object.DestroyImmediate(observer.Skin);
            UnityEngine.Object.DestroyImmediate(listener); UnityEngine.Object.DestroyImmediate(template);
            HeadEar.Claims.Clear();
        }
        return _checks;
    }
}

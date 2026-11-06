using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using ClockStone;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FigureAudioProgram
{
    private const string Loop = "PlaySound_PlagueRatIdle", Shot = "figure-native-one-shot", Ui = "unrelated-ui";
    private static readonly List<Object> Owned = new();
    private static int checks;
    private static AudioController controller = null!;
    private static StreamWriter trace = null!;
    private static T Keep<T>(T o) where T : Object { Owned.Add(o); return o; }
    private static GameObject Go(string name) => Keep(new GameObject(name));
    private static void Check(bool value, string why) { checks++; if (!value) throw new InvalidOperationException(why); }
    private static void Set(Type type, string field, object? target, object? value) => AccessTools.Field(type, field).SetValue(target, value);
    private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .000001f;
    private static AudioClip Clip(string name)
    {
        var clip = Keep(AudioClip.Create(name, 48000, 2, 48000, false)); var data = new float[clip.samples * 2];
        for (int i = 0; i < clip.samples; i++) data[i * 2] = data[i * 2 + 1] = .03f * Mathf.Sin(i * 311f * Mathf.PI * 2f / 48000f);
        clip.SetData(data, 0); return clip;
    }
    private static AudioItem Item(string id, bool loop) => new() {
        Name = id, HapticAudioEnabled = false, HapticVibrationEnabled = false, MaxInstanceCount = 0,
        MinTimeBetweenPlayCalls = 0, Loop = loop ? AudioItem.LoopMode.LoopSubitem : AudioItem.LoopMode.DoNotLoop,
        stopEvents = Array.Empty<AudioStopEvent>(), Volume = .71f, subItems = new[] { new AudioSubItem { Clip = Clip(id), Volume = .83f, PitchShift = 0, Pan2D = 0 } } };
    private static void Setup()
    {
        VRSession.IsRunning = true; VRSession.Harmony = new Harmony("ghvr.figure-audio.fixture." + typeof(FigureAudioProgram).Assembly.GetName().Name);
        NativeFigureAudioPatches.Install(); VRSession.Harmony.PatchAll(typeof(FigureAudioPorts));
        Go("fixture ear").AddComponent<AudioListener>();
        Go("native clock").AddComponent<Chronos.Timekeeper>().AddClock("Root");
        var holder = Go("original AudioController"); holder.SetActive(false); controller = holder.AddComponent<AudioController>();
        Set(typeof(UnitySingleton<AudioController>), "_instance", null, controller); Set(typeof(UnitySingleton<AudioController>), "_awakeSingletonCalled", null, true);
        var prefab = Go("native Audio3DSourceIDLE probe"); var audio = prefab.AddComponent<AudioSource>();
        audio.playOnAwake = false; audio.spatialBlend = 1; audio.panStereo = 0; audio.dopplerLevel = 0; audio.minDistance = 4; audio.maxDistance = 30;
        audio.rolloffMode = AudioRolloffMode.Custom; audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0, 1, 1, 0));
        prefab.AddComponent<AudioObject>(); controller.AudioObjectPrefab = prefab; controller.UsePooledAudioObjects = false;
        controller.AudioCategories = new[] { new AudioCategory(controller) { Name = "Idle_SFX", AudioItems = new[] { Item(Loop, true), Item(Shot, false), Item(Ui, true) } } };
        Set(typeof(UnitySingleton<AudioController>), "_awakeSingletonCalled", null, false); holder.SetActive(true);
        Check(AudioController.IsValidAudioID(Loop), "original native AudioController item lookup succeeds");
    }
    private static ActorBehaviour Figure(string name, Vector3 at)
    {
        var root = Go(name); root.transform.position = at; var manager = root.AddComponent<CharacterManager>();
        manager.CharacterActor = (CPlayerActor)FormatterServices.GetUninitializedObject(typeof(CPlayerActor));
        var model = Go("native animator model"); model.transform.SetParent(root.transform, false);
        return BindActor(manager, model.transform);
    }
    private static ActorBehaviour BindActor(CharacterManager manager, Transform model)
    {
        var native = Go("original ActorPrefab child"); native.transform.SetParent(model, false);
        var figure = native.AddComponent<ActorBehaviour>(); figure.enabled = false;
        figure.Actor = manager.CharacterActor; manager.CharacterActorGO = native; return figure;
    }
    private static Transform Root(ActorBehaviour actor) => actor.GetComponentInParent<CharacterManager>().transform;
    private static SFXOnEnable Idle(ActorBehaviour figure, float delay = 0)
    {
        var child = Go("p_PlagueRat_Idle"); child.SetActive(false); child.transform.SetParent(figure.transform.parent, false); child.transform.localPosition = new Vector3(.2f, .1f, -.1f);
        var producer = child.AddComponent<SFXOnEnable>(); Set(typeof(SFXOnEnable), "m_AudioEvent", producer, Loop); Set(typeof(SFXOnEnable), "m_StopOnDisable", producer, true);
        producer.delay = delay; child.SetActive(true); return producer;
    }
    private static IEnumerator Wait(float seconds)
    {
        double start = AudioSettings.dspTime; float wall = Time.realtimeSinceStartup;
        while (AudioSettings.dspTime - start < seconds) { if (Time.realtimeSinceStartup - wall > seconds + 4) throw new Exception("native DSP clock advances"); yield return null; }
    }
    private static IEnumerable<object?> Steps(IEnumerator iterator) { try { while (iterator.MoveNext()) yield return iterator.Current; } finally { (iterator as IDisposable)?.Dispose(); } }
    private static AudioObject[] Playing(string item) => AudioController.GetPlayingAudioObjects(item).ToArray();
    private static void Pulse(AudioObject source) { var f = source.GetComponent<NativeFigureAudioFollower>(); if (f != null) AccessTools.Method(typeof(NativeFigureAudioFollower), "LateUpdate").Invoke(f, null); }
    private readonly struct Picture
    {
        private readonly AudioClip clip; private readonly float volume, pitch, min, max, spatial, pan, doppler; private readonly bool loop; private readonly AudioRolloffMode rolloff;
        internal Picture(AudioObject source) { var s = source.primaryAudioSource; clip = s.clip; volume = s.volume; pitch = s.pitch; min = s.minDistance; max = s.maxDistance; spatial = s.spatialBlend; pan = s.panStereo; doppler = s.dopplerLevel; loop = s.loop; rolloff = s.rolloffMode; }
        internal bool Same(AudioObject source) { var s = source.primaryAudioSource; return ReferenceEquals(clip, s.clip) && volume == s.volume && pitch == s.pitch && min == s.minDistance && max == s.maxDistance && spatial == s.spatialBlend && pan == s.panStereo && doppler == s.dopplerLevel && loop == s.loop && rolloff == s.rolloffMode; }
    }
    public static IEnumerator Run(string output)
    {
        checks = 0; trace = new StreamWriter(Path.Combine(output, "native-figure-sources.txt"));
        try
        {
            Setup(); var rat = Figure("actual figure", new Vector3(3, 1, 2)); var producer = Idle(rat, .12f);
            Check(Playing(Loop).Length == 0, "native delay is not bypassed by follower");
            foreach (var step in Steps(Wait(.25f))) yield return step;
            Check(Playing(Loop).Length == 1, "one native delayed persistent source starts exactly once");
            var source = Playing(Loop)[0]; var authored = new Picture(source); var parent = source.transform.parent;
            Check(source.primaryAudioSource.spatialBlend == 1 && source.primaryAudioSource.minDistance == 4 && source.primaryAudioSource.maxDistance == 30 && source.primaryAudioSource.dopplerLevel == 0, "original 3D idle source settings are retained");
            Check(Near(source.transform.position, producer.transform.position), "original emitter position is retained at native play");
            Check(producer.GetComponentInParent<ActorBehaviour>() == null
                && ReferenceEquals(producer.GetComponentInParent<CharacterManager>().CharacterActorGO, rat.gameObject),
                "fixture uses actual native sibling topology rather than a synthetic ActorBehaviour ancestor");
            Root(rat).position += new Vector3(2, 0, -3); producer.transform.localPosition += new Vector3(0, .7f, 0); Pulse(source);
            Check(Near(source.transform.position, producer.transform.position), "persistent original source follows moving/animated figure emitter");
            Check(source.transform.parent == parent, "following never reparents original pooled AudioObject"); Check(authored.Same(source), "following preserves clip volume pitch loop stereo pan 3D distance doppler and rolloff");
            foreach (var step in Steps(Wait(1.15f))) yield return step;
            Root(rat).position += Vector3.left; Pulse(source);
            Check(Playing(Loop).Length == 1 && Near(source.transform.position, producer.transform.position), "native subitem loop keeps following across a complete clip without duplicate playback");
            producer.gameObject.SetActive(false); foreach (var step in Steps(Wait(.05f))) yield return step;
            Check(source == null || source._stopRequested || !source.IsPlaying(), "original StopOnDisable still stops its own concrete source");

            var a = Figure("same-item actor A", Vector3.right * 4); var pa = Idle(a); foreach (var step in Steps(Wait(.05f))) yield return step; var sa = Playing(Loop).Single();
            var b = Figure("same-item actor B", Vector3.left * 4); var pb = Idle(b); foreach (var step in Steps(Wait(.05f))) yield return step;
            var sb = Playing(Loop).Single(x => !ReferenceEquals(x, sa)); Root(a).position += Vector3.up; Root(b).position += Vector3.forward; Pulse(sa); Pulse(sb!);
            Check(Near(sa.transform.position, pa.transform.position) && Near(sb.transform.position, pb.transform.position), "identical clip on two actors has independent actual producer ownership");
            pa.gameObject.SetActive(false); foreach (var step in Steps(Wait(.05f))) yield return step;
            Check(sb != null && sb.IsPlaying(), "stopping actor A never stops unrelated same-item actor B");
            var reusePosition = new Vector3(11, 2, 7);
            controller.PlayAudioItem(AudioController.GetAudioItem(Ui), 1, reusePosition, null, 0, 0, false, sb);
            var nativeReusedPosition = sb!.transform.position; // Original useExisting path deliberately ignores worldPosition.
            Root(b).position += Vector3.up; Pulse(sb!);
            Check(Near(sb!.transform.position, nativeReusedPosition), "unscoped native pooled reuse cannot retain previous figure follow");

            // The same item/subitem/timestamp can be reused synchronously before an outer producer
            // finally binds its receipt. The native returned object still belongs to the inner play.
            var outer = NativeFigureAudio.Begin(pb, false); var once = AudioController.Play(Loop, pb.transform, null, false);
            var uiGo = Go("unrelated UI producer"); var uiProducer = uiGo.AddComponent<AudioEffectPlayer>(); var inner = NativeFigureAudio.Begin(uiProducer, false);
            controller.PlayAudioItem(AudioController.GetAudioItem(Loop), 1, reusePosition, null, 0, 0, false, once);
            var nativeInnerPosition = once.transform.position;
            NativeFigureAudio.End(inner); NativeFigureAudio.End(outer); Root(b).position += Vector3.up; Pulse(once);
            Check(Near(once.transform.position, nativeInnerPosition), "same-frame same-item reuse before outer finalizer rejects stale generation");

            var emitterGo = Go("native effect emitter"); emitterGo.transform.SetParent(b.transform.parent, false); emitterGo.transform.localPosition = new Vector3(.3f, .4f, .2f);
            var effect = emitterGo.AddComponent<AudioEffectPlayer>(); effect.PlayEffect(Shot); var effectSource = Playing(Shot).Single();
            Check(Near(effectSource.transform.position, effect.transform.position), "listener-anchored original effect request is located at actual figure emitter");
            Check(effectSource.primaryAudioSource.spatialBlend == 1 && effectSource.primaryAudioSource.minDistance == 4, "effect source keeps native authored 3D attenuation");
            var uiEffect = uiGo.GetComponent<AudioEffectPlayer>(); uiEffect.PlayEffect(Shot); var uiSource = Playing(Shot).Single(x => !ReferenceEquals(x, effectSource)); var uiPosition = uiSource.transform.position;
            uiGo.transform.position += Vector3.one; Pulse(uiSource); Check(Near(uiSource.transform.position, uiPosition), "unrelated UI producer remains outside figure follow");
            VRSession.IsRunning = false; effect.PlayEffect(Shot); var flat = Playing(Shot).Single(x => !ReferenceEquals(x, effectSource) && !ReferenceEquals(x, uiSource)); var flatPosition = flat.transform.position;
            Root(b).position += Vector3.one; Pulse(flat); Check(Near(flat.transform.position, flatPosition), "VR-off original figure audio remains untouched");
            VRSession.IsRunning = true; var actorBefore = b.Actor; effect.PlayEffect(Shot); var replaced = Playing(Shot).Last(); var at = replaced.transform.position;
            b.Actor = (CPlayerActor)FormatterServices.GetUninitializedObject(typeof(CPlayerActor)); Root(b).position += Vector3.up; Pulse(replaced);
            Check(Near(replaced.transform.position, at), "actor pooling/reassignment revokes old emitter receipt"); b.Actor = actorBefore;
            effect.PlayEffect(Shot); var destroyed = Playing(Shot).Last(); var before = destroyed.transform.position; Object.DestroyImmediate(emitterGo); Root(b).position += Vector3.up; Pulse(destroyed);
            Check(Near(destroyed.transform.position, before), "destroyed emitter cannot move or stop native source");
            var pendingRoot = Go("native async actor before ActorPrefab"); var pendingManager = pendingRoot.AddComponent<CharacterManager>();
            pendingManager.CharacterActor = (CPlayerActor)FormatterServices.GetUninitializedObject(typeof(CPlayerActor));
            var pendingModel = Go("pending native model"); pendingModel.transform.SetParent(pendingRoot.transform, false);
            pendingManager.CharacterActorGO = b.gameObject; // Pooled stale pointer must never authorize that other actor.
            var pendingEffect = pendingModel.AddComponent<AudioEffectPlayer>(); pendingEffect.PlayEffect(Shot); var pendingSource = Playing(Shot).Last(); var pendingOriginal = pendingSource.transform.position;
            pendingRoot.transform.position += new Vector3(7, 1, 3); Pulse(pendingSource);
            Check(Near(pendingSource.transform.position, pendingOriginal), "pending native manager changes no source position before exact ActorPrefab binds");
            var pendingFigure = BindActor(pendingManager, pendingModel.transform); Pulse(pendingSource);
            Check(Near(pendingSource.transform.position, pendingEffect.transform.position), "native async manager binds exact future ActorPrefab without replay or timer");
            Check(Playing(Shot).Count(x => ReferenceEquals(x, pendingSource)) == 1, "native async binding never dispatches another sound");
            var pendingOldActor = pendingManager.CharacterActor;
            pendingEffect.PlayEffect(Shot); var pendingReassigned = Playing(Shot).Last(); var assignedAt = pendingReassigned.transform.position;
            pendingManager.CharacterActor = (CPlayerActor)FormatterServices.GetUninitializedObject(typeof(CPlayerActor));
            pendingRoot.transform.position += Vector3.right; Pulse(pendingReassigned);
            Check(Near(pendingReassigned.transform.position, assignedAt), "native CharacterManager reassignment revokes prior original actor receipt");
            pendingManager.CharacterActor = pendingOldActor;
            pendingManager.CharacterActorGO = null; pendingEffect.PlayEffect(Shot); var pendingReused = Playing(Shot).Last();
            controller.PlayAudioItem(AudioController.GetAudioItem(Ui), 1, reusePosition, null, 0, 0, false, pendingReused); var reusedPendingAt = pendingReused.transform.position;
            pendingManager.CharacterActorGO = pendingFigure.gameObject; pendingRoot.transform.position += Vector3.up; Pulse(pendingReused);
            Check(Near(pendingReused.transform.position, reusedPendingAt), "source pool reuse before pending native actor binding never resurrects follow");
            var preview = Go("preview model has no native ActorPrefab"); var previewManager = preview.AddComponent<CharacterManager>();
            previewManager.CharacterActor = (CPlayerActor)FormatterServices.GetUninitializedObject(typeof(CPlayerActor));
            var previewEffect = preview.AddComponent<AudioEffectPlayer>(); previewEffect.PlayEffect(Shot); var previewAudio = Playing(Shot).Last(); var previewAt = previewAudio.transform.position;
            preview.transform.position += Vector3.one; Pulse(previewAudio);
            Check(Near(previewAudio.transform.position, previewAt), "preview non-gameplay model remains native without an exact ActorPrefab association");
            Object.DestroyImmediate(previewManager); preview.transform.position += Vector3.one; Pulse(previewAudio);
            Check(Near(previewAudio.transform.position, previewAt), "destroyed manager before ActorPrefab cannot later bind another actor");
            Object.DestroyImmediate(pendingSource.gameObject); pendingRoot.transform.position += Vector3.one;
            Check(pendingSource == null, "destroyed native source remains destroyed without replay");
            var auditGo = Go("inert native forced-pick metadata"); auditGo.SetActive(false);
            var auditHand = auditGo.AddComponent<CardsHandUI>(); AccessTools.Property(typeof(CardsHandUI), "currentMode").SetValue(auditHand, CardHandMode.DiscardCard, null);
            int playingBeforeAudit = Playing(Shot).Length; int logBeforeAudit = VRLog.Lines.Count;
            var priorAudit = NativePickAudioAudit.Begin(auditHand); var audited = AudioController.Play(Shot);
            var callbackError = new InvalidOperationException("original callback identity");
            var auditFinalizer = AccessTools.Method(AccessTools.Inner(typeof(NativeFigureAudioPatches), "PickCommit"), "Finalizer");
            var returnedError = auditFinalizer.Invoke(null, new object?[] { priorAudit, callbackError });
            Check(ReferenceEquals(returnedError, callbackError), "actual audit finalizer preserves original callback exception identity");
            Check(Playing(Shot).Length == playingBeforeAudit + 1 && audited != null,
                "debug audit never suppresses replays or duplicates native audio requests");
            Check(VRLog.Lines.Skip(logBeforeAudit).Any(x => x.Contains("PICK AUDIO transaction=") && x.Contains("request=1") && x.Contains("item='" + Shot + "'")),
                "debug audit records exact real native item/source request within forced-pick scope");
            int offLog = VRLog.Lines.Count; VRLog.WantsDebug = false;
            priorAudit = NativePickAudioAudit.Begin(auditHand); AudioController.Play(Shot); NativePickAudioAudit.End(priorAudit, null);
            Check(VRLog.Lines.Count == offLog, "ordinary logging performs no forced-pick diagnostic formatting or trace"); VRLog.WantsDebug = true;
            VRSession.IsRunning = false; priorAudit = NativePickAudioAudit.Begin(auditHand); AudioController.Play(Shot); NativePickAudioAudit.End(priorAudit, null);
            Check(VRLog.Lines.Count == offLog, "flat-mode native audio remains outside forced-pick audit"); VRSession.IsRunning = true;
            trace.WriteLine("Native runtime " + typeof(SFXOnEnable).Assembly.Location); foreach (string line in VRLog.Lines) trace.WriteLine(line); trace.Flush();
            Check(VRLog.Lines.All(line => !line.Contains("follow failed")), "native producer/observer scopes finish without swallowed ownership failures");
            yield return checks;
        }
        finally
        {
            AudioController.StopAll(0f); VRSession.Harmony?.UnpatchSelf();
            for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) Object.DestroyImmediate(Owned[i]); Owned.Clear();
            Set(typeof(UnitySingleton<AudioController>), "_instance", null, null); Set(typeof(UnitySingleton<AudioController>), "_awakeSingletonCalled", null, false); trace.Dispose();
        }
    }
}

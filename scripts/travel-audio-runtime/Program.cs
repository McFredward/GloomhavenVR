using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClockStone;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TravelAudioProgram
{
    private const string Loop = "PlaySound_UITravelCartLoop", Drum = "PlaySound_UITravelCartStartDrum",
        Encounter = "PlaySound_UIMapEncounter", Reach = "PlaySound_UITravelReachLocation";
    private static int _checks;
    private static readonly List<Object> _owned = new();
    private static AudioController _controller = null!;
    private static PartyToken _token = null!;
    private static MapChoreographer _choreo = null!;
    private static StreamWriter _trace = null!;

    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static T Keep<T>(T item) where T : Object { _owned.Add(item); return item; }
    private static GameObject Go(string name) => Keep(new GameObject(name));
    private static void Set(Type type, string name, object? target, object? value)
        => AccessTools.Field(type, name).SetValue(target, value);
    private static AudioClip Clip(string name, float seconds, float frequency)
    {
        var clip = Keep(AudioClip.Create(name, (int)(48000 * seconds), 1, 48000, false));
        var data = new float[clip.samples];
        for (int i = 0; i < data.Length; i++) data[i] = .08f * Mathf.Sin(i * frequency * Mathf.PI * 2f / 48000);
        clip.SetData(data, 0); return clip;
    }
    private static AudioItem Item(string name, AudioClip clip, bool loop)
        => new AudioItem { Name = name, HapticAudioEnabled = false, HapticVibrationEnabled = false,
            Loop = loop ? AudioItem.LoopMode.LoopSubitem : AudioItem.LoopMode.DoNotLoop,
            MaxInstanceCount = 0, MinTimeBetweenPlayCalls = 0, stopEvents = Array.Empty<AudioStopEvent>(),
            SubItemPickMode = loop ? AudioPickSubItemMode.StartLoopSequenceWithFirst : AudioPickSubItemMode.RandomNotSameTwice,
            subItems = new[] { new AudioSubItem { Clip = clip } } };

    private static void Setup()
    {
        _trace.WriteLine("Unchanged publisher audio runtime: " + typeof(AudioController).Assembly.Location);
        _trace.WriteLine("Probe clips replace original waveforms; MinTimeBetweenPlayCalls=0 removes fixture timing throttles. Original loop, unlimited multiplicity and stop-event behavior remain.");
        VRSession.Harmony = new Harmony("ghvr.travel-audio.fixture." + typeof(TravelAudioProgram).Assembly.GetName().Name);
        VRSession.Harmony.PatchAll(typeof(TravelPorts));
        NativeMapTravelAudio.Install();
        Go("fixture ear").AddComponent<AudioListener>();
        var keeper = Go("native clock").AddComponent<Chronos.Timekeeper>(); keeper.AddClock("Root");
        var controllerGo = Go("native AudioController"); controllerGo.SetActive(false);
        _controller = controllerGo.AddComponent<AudioController>();
        // Bootstrap only the audio singleton; no main-game controllers or callbacks are run.
        Set(typeof(UnitySingleton<AudioController>), "_instance", null, _controller);
        Set(typeof(UnitySingleton<AudioController>), "_awakeSingletonCalled", null, true);
        var prefab = Go("native audio prefab");
        var audio = prefab.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0;
        prefab.AddComponent<AudioObject>();
        _controller.AudioObjectPrefab = prefab; _controller.UsePooledAudioObjects = false;
        var loopClip = Clip("probe cart waveform", 1f, 311f);
        _controller.AudioCategories = new[] { new AudioCategory(_controller) { Name = "UI", AudioItems = new[] {
            Item(Loop, loopClip, true), Item(Drum, Clip("probe drum", .12f, 811f), false),
            Item(Encounter, Clip("probe encounter", .6f, 521f), false),
            Item(Reach, Clip("probe reach", .15f, 1049f), false),
            Item("unrelated-native-loop", Clip("probe unrelated", 1f, 137f), true) } } };
        Set(typeof(UnitySingleton<AudioController>), "_awakeSingletonCalled", null, false);
        controllerGo.SetActive(true);
        _token = Go("native token boundary").AddComponent<PartyToken>(); _token.MoveSpeed = 2;
        _choreo = Go("native coroutine host").AddComponent<MapChoreographer>(); _choreo.enabled = false;
        Set(typeof(Singleton<MapChoreographer>), "_instance", null, _choreo);
        MapRoomDriver.Active = true;
        Check(AudioController.IsValidAudioID(Loop), "real native item table resolves serialized cart identity");
        Check(AudioController.GetAudioItem(Loop).Loop == AudioItem.LoopMode.LoopSubitem
            && AudioController.GetAudioItem(Loop).MaxInstanceCount == 0,
            "probe item retains original unlimited native looping behavior");
    }
    private static MapTimedMovementFlow Timed()
    {
        var flow = Go("native timed flow").AddComponent<MapTimedMovementFlow>();
        var config = Keep(ScriptableObject.CreateInstance<MapTimedFlowConfig>());
        config.MovingAudioItem = Loop; config.StartMovingAudioItem = Drum; config.ReachDestinationAudioItem = Reach;
        config.FadeOutMovingAudioItemDuration = .03f;
        Set(typeof(MapMovementFlow<MapTimedFlowConfig>), "mapConfig", flow, config);
        return flow;
    }
    private static MapPointsMovementFlow Points()
    {
        var flow = Go("native points flow").AddComponent<MapPointsMovementFlow>();
        var config = Keep(ScriptableObject.CreateInstance<MapPointsFlowConfig>());
        config.MovingAudioItem = Loop; config.ReachDestinationAudioItem = Reach;
        config.FadeOutMovingAudioItemDuration = .03f;
        Set(typeof(MapMovementFlow<MapPointsFlowConfig>), "mapConfig", flow, config);
        Set(typeof(MapMovementFlow), "m_WaypointsToEnd", flow, new[] { Vector3.zero, Vector3.right * 10 });
        return flow;
    }
    private static AudioObject[] Playing(string item) => AudioController.GetPlayingAudioObjects(item).ToArray();
    private static AudioObject[] Objects(string item) => RegisteredComponentController.GetAllOfType<AudioObject>()
        .Where(a => a != null && a.gameObject.activeInHierarchy && a.audioID == item
            && !a._stopRequested && !a._IsInactive).ToArray();
    private static IEnumerator Wait(float seconds)
    {
        double start = AudioSettings.dspTime; float wall = Time.realtimeSinceStartup;
        while (AudioSettings.dspTime - start < seconds)
        {
            if (Time.realtimeSinceStartup - wall >= seconds + 4)
                throw new InvalidOperationException("real Unity DSP clock advances with forceAudio");
            yield return null;
        }
    }
    private static void Snapshot(string label)
    {
        _trace.WriteLine(label + " dsp=" + AudioSettings.dspTime.ToString("F6"));
        foreach (var source in Object.FindObjectsOfType<AudioObject>())
            _trace.WriteLine($" object={source.GetInstanceID()} item={source.audioID} playing={source.IsPlaying()} paused={source.IsPaused()} scheduled={source.scheduledPlayingAtDspTime:F6} start={source.startedPlayingAtTime:F6} samples={source.primaryAudioSource.timeSamples} loop={source.primaryAudioSource.loop} clip={source.primaryAudioSource.clip?.name}");
        _trace.Flush();
    }
    private static void ResetAudio()
    {
        AudioController.StopAll(0f); _choreo.StopAllCoroutines(); TravelPorts.Delayed.Clear();
        TravelPorts.Encounter = null; TravelPorts.Arrival = null;
    }
    private static void DestroyFlow(MapMovementFlow flow)
    { Object.DestroyImmediate(flow.gameObject); }

    public static IEnumerator Run(string output)
    {
        _checks = 0; _trace = new StreamWriter(Path.Combine(output, "actual-audio-sources.txt"));
        try
        {
            Setup();
            var flow = Timed(); int encounterCalls = 0, arriveCalls = 0;
            Action onEncounter = () => encounterCalls++;
            flow.MovePartyToEncounter(_token, onEncounter);
            Check(ReferenceEquals(TravelPorts.Encounter, onEncounter) && encounterCalls == 0,
                "native encounter callback remains deferred at original movement boundary");
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            Check(Playing(Loop).Length == 1, "one real native cart AudioSource plays before encounter");
            var first = Playing(Loop)[0]; int firstSamples = first.primaryAudioSource.timeSamples;
            var eventGo = Go("native event producer"); eventGo.SetActive(false);
            var panel = eventGo.AddComponent<UIEventPanel>(); panel.PlayShowAudio();
            Check(Playing(Loop).Length == 1 && Playing(Encounter).Length == 1,
                "original encounter item leaves the first cart loop alive while its different cue plays");
            TravelPorts.Encounter!();
            Check(encounterCalls == 1, "native encounter callback executes exactly once only when movement finishes");
            Set(typeof(UIEventPanel), "onEventCompleted", panel, (Action)(() => flow.MovePartyFromEncounterToDestination(_token, () => arriveCalls++)));
            AccessTools.Method(typeof(UIEventPanel), "OnDistributedRewards").Invoke(panel, null);
            foreach (var unused in Enumerate(Wait(.08f))) yield return unused;
            Snapshot("after actual original event completion and timed continuation");
            Check(Playing(Loop).Length == 1, "resumed native timed travel has exactly one cart source");
            Check(first == null || !first.IsPlaying(), "previous owned source stops before native replacement");
            Check(Playing(Encounter).Length == 1 && Playing(Drum).Length == 1,
                "intentional encounter and completion drum cues remain simultaneous with the cart");
            Check(firstSamples > 0 && Playing(Loop)[0].primaryAudioSource.timeSamples > 0,
                "actual old and replacement Unity sources both advanced native PCM sample cursors");
            Check(arriveCalls == 0, "audio repair never invokes native arrival early");
            TravelPorts.Arrival!();
            foreach (var unused in Enumerate(Wait(.09f))) yield return unused;
            Check(arriveCalls == 1 && Playing(Loop).Length == 0,
                "native arrival closure alone stops cart audio and invokes original continuation once");
            Check(Playing(Reach).Length == 1, "native destination cue remains audible");
            DestroyFlow(flow); ResetAudio();

            // Native points flow has the same producer asymmetry and still owns its delayed arrival.
            var points = Points(); points.MovePartyToEncounter(_token, onEncounter);
            foreach (var unused in Enumerate(Wait(.04f))) yield return unused;
            points.MovePartyFromEncounterToDestination(_token, () => arriveCalls++);
            foreach (var unused in Enumerate(Wait(.04f))) yield return unused;
            Check(Playing(Loop).Length == 1, "native points continuation also replaces its prior concrete cart request");
            Check(TravelPorts.Delayed.Count == 1 && arriveCalls == 1,
                "points delayed arrival cue and original movement callback stay independent");
            TravelPorts.Delayed[0](); TravelPorts.Arrival!();
            foreach (var unused in Enumerate(Wait(.06f))) yield return unused;
            Check(Playing(Loop).Length == 0 && arriveCalls == 2,
                "native points delayed sound and explicit movement completion retain authority");
            DestroyFlow(points); ResetAudio();

            flow = Timed(); flow.MovePartyFromOriginToDestination(_token, () => arriveCalls++);
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            flow.MovePartyFromNarrativeToDestination(_token, () => arriveCalls++);
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            Check(Playing(Loop).Length == 1, "timed origin and narrative entrypoints reach the same owned native producer");
            Check(arriveCalls == 2, "replacing timed audio never commits native narrative or destination callbacks");
            DestroyFlow(flow); ResetAudio();
            points = Points(); points.MovePartyFromOriginToDestination(_token, () => arriveCalls++);
            foreach (var unused in Enumerate(Wait(.04f))) yield return unused;
            points.MovePartyFromNarrativeToDestination(_token, () => arriveCalls++);
            foreach (var unused in Enumerate(Wait(.04f))) yield return unused;
            Check(Playing(Loop).Length == 1, "points origin and narrative entrypoints reach the same owned native producer");
            DestroyFlow(points); ResetAudio();

            // A DSP-scheduled first loop must be canceled even before its drum finishes.
            flow = Timed(); flow.MovePartyToEncounter(_token, onEncounter);
            var pending = Objects(Loop).Single();
            Check(pending.scheduledPlayingAtDspTime > AudioSettings.dspTime,
                "actual native PlaySeqSounds creates the first loop at a future DSP time");
            flow.MovePartyFromEncounterToDestination(_token, () => arriveCalls++);
            foreach (var unused in Enumerate(Wait(.23f))) yield return unused;
            Snapshot("continued before first drum finished");
            Check(Playing(Loop).Length == 1, "pending old native loop never reappears after resume");
            DestroyFlow(flow); ResetAudio();

            // Another producer using the same ID is not this flow's owned sound.
            AudioControllerUtils.PlaySound(Loop); var independent = Playing(Loop).Single();
            AudioControllerUtils.PlaySound("unrelated-native-loop");
            flow = Timed(); flow.MovePartyToEncounter(_token, onEncounter);
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            Check(Playing(Loop).Length == 2, "independent same-item producer and native travel coexist");
            flow.MovePartyFromEncounterToDestination(_token, () => { });
            foreach (var unused in Enumerate(Wait(.05f))) yield return unused;
            Check(independent != null && independent.IsPlaying() && Playing(Loop).Length == 2,
                "replacement preserves independent producer using the identical audio item");
            Check(Playing("unrelated-native-loop").Length == 1, "different native ambience remains playing");
            DestroyFlow(flow); ResetAudio();

            // A single native request may intentionally contain multiple simultaneous layers.
            var item = AudioController.GetAudioItem(Loop);
            item.SubItemPickMode = AudioPickSubItemMode.AllSimultaneously;
            item.subItems = new[] { item.subItems[0], new AudioSubItem { Clip = Clip("probe second cart layer", 1f, 443f) } };
            _controller.InitializeAudioItems();
            flow = Timed(); flow.MovePartyToEncounter(_token, onEncounter);
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            Check(Playing(Loop).Length == 2, "native simultaneous group retains both authored layers");
            flow.MovePartyFromEncounterToDestination(_token, () => { });
            foreach (var unused in Enumerate(Wait(.05f))) yield return unused;
            Check(Playing(Loop).Length == 2 && Playing(Loop).Select(a => a.primaryAudioSource.clip).Distinct().Count() == 2,
                "replacement cancels every prior layer and retains every new native layer");
            DestroyFlow(flow); ResetAudio(); item.subItems = new[] { item.subItems[0] };
            item.SubItemPickMode = AudioPickSubItemMode.StartLoopSequenceWithFirst; _controller.InitializeAudioItems();

            // A reused native object is no longer an old receipt, even with the same item/subitem.
            flow = Timed(); flow.MovePartyToEncounter(_token, onEncounter);
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            var reused = Playing(Loop).Single(); double oldStart = reused.startedPlayingAtTime;
            reused.PlayNow(Loop);
            Check(reused.startedPlayingAtTime != oldStart, "real native object reuse changes its playback stamp");
            flow.MovePartyFromEncounterToDestination(_token, () => { });
            foreach (var unused in Enumerate(Wait(.05f))) yield return unused;
            Check(reused != null && reused.IsPlaying() && Playing(Loop).Length == 2,
                "playback stamp prevents stale ownership from stopping a reused same-item source");
            DestroyFlow(flow); ResetAudio();

            points = Points(); points.MovePartyToEncounter(_token, onEncounter);
            reused = Objects(Loop).Single(); oldStart = reused.startedPlayingAtTime;
            AudioSubItem sameSubitem = reused.subItem;
            reused.PlayNow(Loop); // No yield/Update: native systemTime cannot advance here.
            _trace.WriteLine($"same-frame reuse object={reused.GetInstanceID()} before={oldStart:R} after={reused.startedPlayingAtTime:R} identicalSubitem={ReferenceEquals(reused.subItem, sameSubitem)} item={reused.audioID}");
            Check(reused.startedPlayingAtTime == oldStart && ReferenceEquals(reused.subItem, sameSubitem)
                && reused.audioID == Loop, "real same-frame native reuse keeps identical timestamp, item and subitem");
            points.MovePartyFromEncounterToDestination(_token, () => { });
            foreach (var unused in Enumerate(Wait(.05f))) yield return unused;
            Snapshot("same-frame same-subitem same-ID native replay remains independently owned");
            Check(reused != null && reused.IsPlaying() && Playing(Loop).Length == 2,
                "per-object generation preserves same-frame same-item native replay");
            DestroyFlow(points); ResetAudio();

            // The scope unwinds through original exceptions and map activity changes.
            flow = Timed(); TravelPorts.ThrowFromEncounter = true; TravelPorts.LeaveRoomDuringEncounter = true;
            try { flow.MovePartyToEncounter(_token, onEncounter); throw new Exception("native failure was hidden"); }
            catch (InvalidOperationException ex) { Check(ex.Message == "fixture native movement boundary failed", "native exception propagates unchanged"); }
            finally { TravelPorts.ThrowFromEncounter = false; TravelPorts.LeaveRoomDuringEncounter = false; }
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            independent = AudioController.Play(Loop);
            MapRoomDriver.Active = true; flow.MovePartyFromEncounterToDestination(_token, () => { });
            foreach (var unused in Enumerate(Wait(.05f))) yield return unused;
            Check(independent != null && independent.IsPlaying() && Playing(Loop).Length == 2,
                "exception finalizer restores ownership scope after room activity changes");
            DestroyFlow(flow); ResetAudio();

            // Release follows established ownership after stand-down, including paused sources
            // omitted by the native OnDestroy IsPlaying/StopSound query.
            flow = Timed(); flow.MovePartyToEncounter(_token, onEncounter);
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            var paused = Playing(Loop).Single(); paused.Pause(0f); MapRoomDriver.Active = false;
            Check(paused.IsPaused() && !paused.IsPlaying(), "native paused source falls outside native playing-only teardown query");
            DestroyFlow(flow); paused.Unpause();
            foreach (var unused in Enumerate(Wait(.05f))) yield return unused;
            Check(paused == null || !paused.IsPlaying(), "destroyed flow cancels its owned paused loop after room stand-down");
            ResetAudio();

            // In flat presentation the exact original producers retain their existing behavior.
            flow = Timed(); flow.MovePartyToEncounter(_token, onEncounter);
            foreach (var unused in Enumerate(Wait(.22f))) yield return unused;
            flow.MovePartyFromEncounterToDestination(_token, () => { });
            foreach (var unused in Enumerate(Wait(.05f))) yield return unused;
            Check(Playing(Loop).Length == 2, "flat native travel remains outside the VR ownership repair");
            DestroyFlow(flow); ResetAudio();
            Check(VRLog.Lines.Count > 0 && VRLog.Lines.Count < 20, "normal diagnostics remain bounded to replaced producer requests");
            yield return _checks;
        }
        finally
        {
            _trace.Dispose();
            if (_choreo != null) _choreo.StopAllCoroutines();
            foreach (var flow in Object.FindObjectsOfType<MapMovementFlow>()) Object.DestroyImmediate(flow.gameObject);
            foreach (var source in Object.FindObjectsOfType<AudioObject>()) Object.DestroyImmediate(source.gameObject);
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear(); Set(typeof(Singleton<MapChoreographer>), "_instance", null, null);
            VRSession.Harmony?.UnpatchSelf();
        }
    }

    private static IEnumerable<object?> Enumerate(IEnumerator coroutine)
    { while (coroutine.MoveNext()) yield return coroutine.Current; }
}

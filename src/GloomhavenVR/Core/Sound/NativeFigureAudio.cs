using System;
using System.Collections.Generic;
using ClockStone;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Follow the actual native figure emitter with its original pooled playback.
/// No clip is replayed, stopped or deduplicated; native ownership and audio settings remain native.</summary>
internal static class NativeFigureAudio
{
    internal sealed class Capture
    {
        internal readonly Capture? Previous;
        internal readonly Component Producer;
        internal ActorBehaviour? Figure;
        internal readonly CharacterManager? Manager;
        internal readonly CActor Actor;
        internal readonly bool PreserveOffset;
        internal readonly List<Receipt> Objects = new();
        internal Capture(Capture? previous, Component producer, ActorBehaviour? figure,
            CharacterManager? manager, CActor actor, bool offset)
        { Previous = previous; Producer = producer; Figure = figure; Manager = manager; Actor = actor; PreserveOffset = offset; }
    }
    internal readonly struct Receipt
    {
        internal readonly NativeFigureAudioFollower Follower;
        internal readonly ulong Generation;
        internal Receipt(NativeFigureAudioFollower follower) { Follower = follower; Generation = follower.Generation; }
    }
    internal readonly struct Scope
    {
        internal readonly Capture? Previous, Current;
        internal Scope(Capture? previous, Capture? current) { Previous = previous; Current = current; }
    }
    private static Capture? _capture;
    private static int _failures, _reported;

    internal static Scope Begin(Component producer, bool preserveOffset, bool eligible = true)
    {
        Capture? previous = _capture; _capture = null;
        try
        {
            if (eligible && VRSession.IsRunning && producer != null && producer.gameObject.activeInHierarchy)
            {
                // Native Choreographer creates ActorPrefab beneath the animator AFTER model
                // initialization. Model audio is usually a SIBLING, not an ActorBehaviour child.
                CharacterManager? manager = producer.GetComponentInParent<CharacterManager>();
                ActorBehaviour? figure = manager == null ? producer.GetComponentInParent<ActorBehaviour>() : null;
                CActor? actor = manager != null ? manager.CharacterActor : figure != null ? figure.Actor : null;
                if (actor != null)
                    _capture = new Capture(previous, producer, figure, manager, actor, preserveOffset);
            }
        }
        catch (Exception ex) { Failure(ex); }
        return new Scope(previous, _capture);
    }

    internal static void End(Scope scope)
    {
        _capture = scope.Previous;
        Capture? capture = scope.Current;
        if (capture == null) return;
        try
        {
            // Apply after the complete native producer, including AudioItem's final overrides.
            // Every subitem/layer is retained; the native producer still owns its stop callback.
            foreach (Receipt receipt in capture.Objects)
                if (receipt.Follower != null && receipt.Follower.Generation == receipt.Generation)
                    receipt.Follower.Bind(capture, receipt.Generation);
        }
        catch (Exception ex) { Failure(ex); }
    }

    internal static void Observe(AudioObject? source)
    {
        if (source == null) return;
        try
        {
            NativeFigureAudioFollower? follower = source.GetComponent<NativeFigureAudioFollower>();
            // Invalidate on EVERY actual reuse, even an unscoped UI play using the same clip,
            // subitem and ClockStone frame timestamp. No new component for unrelated playback.
            if (follower != null) follower.Reused();
            Capture? capture = _capture;
            if (capture == null) return;
            if (follower == null) { follower = source.gameObject.AddComponent<NativeFigureAudioFollower>(); follower.Source = source; follower.Reused(); }
            for (int i = 0; i < capture.Objects.Count; i++)
                if (ReferenceEquals(capture.Objects[i].Follower, follower))
                { capture.Objects[i] = new Receipt(follower); return; }
            capture.Objects.Add(new Receipt(follower));
        }
        catch (Exception ex) { Failure(ex); }
    }

    internal enum Admission { Invalid, Pending, Live }
    internal static Admission Admit(Capture owner)
    {
        if (!VRSession.IsRunning || owner.Producer == null || !owner.Producer.gameObject.activeInHierarchy)
            return Admission.Invalid;
        if (!ReferenceEquals(owner.Manager, null) && owner.Manager == null) return Admission.Invalid;
        ActorBehaviour? figure;
        if (owner.Manager != null)
        {
            if (!owner.Manager.gameObject.activeInHierarchy
                || !ReferenceEquals(owner.Producer.GetComponentInParent<CharacterManager>(), owner.Manager)
                || !ReferenceEquals(owner.Manager.CharacterActor, owner.Actor)) return Admission.Invalid;
            GameObject? native = owner.Manager.CharacterActorGO;
            // Pending receipt changes nothing until the exact future native ActorPrefab binds.
            // Preview/UI models never acquire that pointer. There is no timer or replay request.
            if (native == null) return ReferenceEquals(owner.Figure, null) ? Admission.Pending : Admission.Invalid;
            figure = native.GetComponent<ActorBehaviour>();
            if (figure == null || !figure.transform.IsChildOf(owner.Manager.transform)
                || !ReferenceEquals(figure.Actor, owner.Actor))
                return ReferenceEquals(owner.Figure, null) ? Admission.Pending : Admission.Invalid;
        }
        else figure = owner.Producer.GetComponentInParent<ActorBehaviour>();
        if (figure == null || !figure.gameObject.activeInHierarchy || !ReferenceEquals(figure.Actor, owner.Actor)
            || !ReferenceEquals(owner.Figure, null) && !ReferenceEquals(owner.Figure, figure)) return Admission.Invalid;
        owner.Figure = figure;
        return Admission.Live;
    }

    internal static void Report(Capture capture, AudioObject source)
    {
        // A bounded normal-level receipt makes ordinary reports useful without a per-step stream.
        if (_reported++ < 12)
            VRLog.Note("Sound", $"FIGURE AUDIO bound original native source: figure={capture.Figure?.name}, emitter={capture.Producer.name}, item='{source.audioID}', loop={source.primaryAudioSource.loop}, spatial={source.primaryAudioSource.spatialBlend:F2}. Native clip, settings and stop ownership retained.");
    }
    internal static void Failure(Exception ex)
    {
        if (_failures++ < 3)
            VRLog.Alert("Sound", $"FIGURE AUDIO follow failed: {ex.GetType().Name}: {ex.Message}. Original playback remains active.");
    }
}

/// <summary>A receipt is per actual native play generation, never merely per clip or timestamp.
/// The component stays on the native pooled source; it never reparents that source.</summary>
internal sealed class NativeFigureAudioFollower : MonoBehaviour
{
    internal AudioObject? Source;
    internal ulong Generation { get; private set; }
    private NativeFigureAudio.Capture? _owner;
    private AudioSubItem? _subItem;
    private double _started;
    private Vector3 _localOffset;
    private bool _reported;
    internal void Reused() { if (++Generation == 0) Generation++; _owner = null; }
    internal void Bind(NativeFigureAudio.Capture owner, ulong generation)
    {
        if (generation != Generation || Source == null || NativeFigureAudio.Admit(owner) == NativeFigureAudio.Admission.Invalid) return;
        _owner = owner; _subItem = Source.subItem; _started = Source.startedPlayingAtTime; _reported = false;
        _localOffset = owner.PreserveOffset ? owner.Producer.transform.InverseTransformPoint(Source.transform.position) : Vector3.zero;
        Follow();
    }
    private void LateUpdate()
    {
        try { Follow(); }
        catch (Exception ex) { _owner = null; NativeFigureAudio.Failure(ex); }
    }
    private void Follow()
    {
        NativeFigureAudio.Capture? owner = _owner;
        if (owner == null) return;
        NativeFigureAudio.Admission admission = NativeFigureAudio.Admit(owner);
        if (Source == null || !Source.gameObject.activeInHierarchy || admission == NativeFigureAudio.Admission.Invalid
            || !ReferenceEquals(Source.subItem, _subItem) || Source.startedPlayingAtTime != _started)
        { _owner = null; return; }
        if (admission == NativeFigureAudio.Admission.Pending) return;
        Vector3 target = owner.Producer.transform.TransformPoint(_localOffset);
        if (float.IsNaN(target.x) || float.IsInfinity(target.x) || float.IsNaN(target.y)
            || float.IsInfinity(target.y) || float.IsNaN(target.z) || float.IsInfinity(target.z)) return;
        Source.transform.position = target;
        if (!_reported) { _reported = true; NativeFigureAudio.Report(owner, Source); }
    }
    private void OnDisable() => _owner = null;
}

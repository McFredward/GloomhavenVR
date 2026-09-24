using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A station's cosmetic face. Native body clips are sampled first; one authority then
/// supplies gaze, and the same expression clock/voice cue drives every observer.</summary>
internal sealed class TownServiceFace
{
    private readonly byte _service;
    private readonly Transform _root;
    private readonly TownServiceFaceRig _rig;
    private readonly TownServiceFaceAttention _attention = new();
    private TownFacePose _shown;
    private float _lastRemote;
    private int _shownAuthor;
    private bool _hasRemote;
    private bool _prepared;
    private bool _preparedEngaged, _workFocusBound;
    private Transform? _workFocus;
    private Vector3? _preparedTarget;
    private static readonly bool[] ReportedMissing = new bool[4];
    internal TownServiceFace(Transform root, byte service)
    {
        _root = root; _service = service; _rig = new TownServiceFaceRig(root);
        if (!_rig.Complete && service >= 1 && service <= 3 && !ReportedMissing[service])
        {
            ReportedMissing[service] = true;
            GloomhavenVR.Core.VRLog.Warn("TownServices", "NPC facial rig incomplete for service " + service
                + "; install the matching town bundle. Native service interaction remains available.");
        }
    }
    /// <summary>Each local owner may inspect stock inside the same attention volume even
    /// while the resident's shared gaze is attending to another nearby player.</summary>
    internal bool IsLocalVisitorNear(bool wasNear)
    {
        Camera? camera = GloomhavenVR.Rig.VRRigDriver.HeadCamera;
        if (camera == null || !camera.gameObject.activeInHierarchy) return false;
        Vector3 delta = camera.transform.position - _rig.EyePosition;
        float scale = Mathf.Max(.01f, _root.lossyScale.x);
        float reach = (wasNear ? 2.9f : 2.4f) * scale;
        if (delta.sqrMagnitude < .01f * scale * scale || delta.sqrMagnitude > reach * reach
            || (Quaternion.Inverse(_rig.OpticalRotation) * delta).z < -.1f * scale) return false;
        return !Physics.Raycast(_rig.EyePosition, delta.normalized, Mathf.Max(0f, delta.magnitude - .08f * scale),
            Physics.DefaultRaycastLayers & ~(1 << GloomhavenVR.Core.VRLayers.ModLayer), QueryTriggerInteraction.Ignore);
    }
    internal bool PrepareActivityAttention(bool wasEngaged)
    {
        Vector3? target = _attention.Select(_service, _root, _rig.OpticalRotation, _rig.EyePosition);
        float reach = (wasEngaged ? 2.9f : 2.4f) * _root.lossyScale.x;
        bool engaged = target.HasValue && (_attention.Visitor || (target.Value - _rig.EyePosition).sqrMagnitude <= reach * reach);
        _preparedEngaged = engaged;
        _preparedTarget = engaged ? target : null;
        _prepared = true; return engaged;
    }
    internal void BeforeBodySample() => _rig.BeforeBodySample();
    internal void Seed(in TownFacePose pose, int author, float elapsed)
    {
        if (_hasRemote) return; // The currently displayed predecessor is newer than another peer's old authority snapshot.
        _hasRemote = true;
        _shown = pose; _shownAuthor = author; _shown.SpeechAge = Mathf.Min(3600f, pose.SpeechAge + elapsed);
        TownServiceFaceSpeech.Observer?.Invoke(_service, author, _shown.Cue, _shown.Generation, _shown.SpeechAge, _rig.Head);
    }
    internal TownFacePose Tick(bool author, bool received, int authorId, in TownFacePose remote, float elapsed, float clock)
    {
        Vector3 mouth;
        if (author)
        {
            _shownAuthor = authorId;
            Vector3? target = _prepared ? _preparedTarget : _attention.Select(_service, _root, _rig.OpticalRotation, _rig.EyePosition);
            if (_prepared && !_preparedEngaged)
            {
                // Activity is applied between preparation and this final face sample.
                // Look at this frame's actual coin/spell, not yesterday's fixed ledger
                // point. Only the elected author computes gaze; peers replay its pose.
                if (!_workFocusBound) { _workFocusBound = true; _workFocus = _root.Find("ActivityWorkFocus"); }
                target = _workFocus != null ? _workFocus.position : _root.TransformPoint(TownServiceActivityMotion.RestFocus(_service));
            }
            _prepared = false;
            _shown = TownServiceFaceMotion.Aim(_rig.OpticalRotation, _root.lossyScale.x, _rig.HeadPosition, _rig.LeftPosition, _rig.RightPosition, target, in _shown, Time.unscaledDeltaTime);
            _shown.Cue = 0; _shown.SpeechAge = 0f; _shown.Jaw = _shown.Wide = _shown.Round = 0;
            mouth = Vector3.zero;
            if (TownServiceFaceSpeech.Sampler != null && TownServiceFaceSpeech.Sampler(_service,
                    out ushort cue, out uint generation, out float age, out mouth)
                && !float.IsNaN(age) && !float.IsInfinity(age))
            {
                _shown.Cue = cue; _shown.Generation = generation; _shown.SpeechAge = Mathf.Clamp(age, 0f, 3600f);
                _shown.Jaw = Encode(mouth.x); _shown.Wide = Encode(mouth.y); _shown.Round = Encode(mouth.z);
            }
            if (_shown.Cue == 0) { mouth = Vector3.zero; _shown.Jaw = _shown.Wide = _shown.Round = 0; }
        }
        else
        {
            if (received)
            { _shown = remote; _hasRemote = true; _shownAuthor = authorId; _lastRemote = Time.unscaledTime; }
            else if (Time.unscaledTime - _lastRemote > .25f)
                _shown = TownServiceFaceMotion.Aim(_rig.OpticalRotation, _root.lossyScale.x,
                    _rig.HeadPosition, _rig.LeftPosition, _rig.RightPosition, null, in _shown, Time.unscaledDeltaTime);
            _shown.SpeechAge = Mathf.Min(3600f, _shown.SpeechAge + (received ? elapsed : Time.unscaledDeltaTime));
            mouth = TownServiceFaceSpeech.Curve != null
                ? TownServiceFaceSpeech.Curve(_service, _shown.Cue, _shown.SpeechAge)
                : Vector3.zero;
        }
        TownServiceFaceSpeech.Observer?.Invoke(_service, _shownAuthor, _shown.Cue, _shown.Generation, _shown.SpeechAge, _rig.Head);
        TownServiceFacePose pose = TownServiceFaceMotion.Evaluate(in _shown, clock, _service, mouth);
        _rig.Apply(in pose);
        return _shown;
    }
    private static byte Encode(float value) => (byte)Mathf.RoundToInt(TownServiceFaceMotion.Weight(value) * 255f);
}

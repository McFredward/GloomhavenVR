using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Compat;

/// <summary>The keys a lesson can point at. These are the PART names the controller prefabs use
/// (see unity/GloomhavenVR.Assets/Assets/Bundle/Controllers/README.md) — every profile in the
/// webxr-input-profiles set names them identically, which is why one lesson table works on all
/// of them.</summary>
internal static class ControllerKey
{
    internal const string Trigger = "trigger";
    internal const string Squeeze = "squeeze";
    internal const string Thumbstick = "thumbstick";
    internal const string Primary = "button_primary";   // A on the right, X on the left
    internal const string Secondary = "button_secondary"; // B on the right, Y on the left
}

/// <summary>
/// THE PLAYER'S OWN CONTROLLER, IN THE PLAYER'S OWN HAND — the hand mesh steps aside for the
/// duration of the controls lesson and the real device takes its place, with the key the lesson
/// is talking about lit up on it.
///
/// <para>WHY THE DEVICE POSE AND NOT THE HAND ROOT — MEASURED, after the reasoned version of this
/// paragraph was very nearly wrong. The model is parented to <c>VRHand.transform</c>, which carries
/// the raw <c>devicePosition/deviceRotation</c>. <c>_handRoot</c> below it holds the hand ART at a
/// configurable visual offset and carries the per-style hand SCALE
/// ([Hands] GloveScale/PlateScale/ArcaneScale); a controller is a real object of a real size, so
/// hanging it there would move and resize a physical device to match a stylistic choice about
/// gloves.</para>
///
/// <para>THE PROOF IS THE PROFILE'S OWN POINTING_POSE NODE (Editor/PreviewControllers.cs renders
/// the check). The generic profile ships the OpenXR AIM pose relative to the GRIP pose its meshes
/// are authored around; after the glTF→Unity conversion it lands 7.2 cm forward, 3.0 cm below and
/// pitched 17.5° down from +Z — exactly OpenXR's grip-to-aim relationship. So the converted model
/// frame IS the frame Unity reports, and identity is right.</para>
///
/// <para>THE FIRST RENDER APPEARED TO REFUTE THAT, and the refutation was a red herring worth
/// recording: the controller did not sit in the glove's hand. The glove is not evidence about
/// where a device is. <c>Anchor_Grab</c> sits 4 cm from the device pose and about 90° off it,
/// because the hand art is stylised and hand-seated — the shipped glove carries a 7 cm comfort
/// SPREAD the user dialled in. Parenting to <c>Anchor_Grab</c> instead was tried and buries the
/// controller in the wrist. One visible consequence remains and is correct: when the lesson starts,
/// the controller appears where the player's REAL controller is, which is not quite where the
/// stylised glove was.</para>
///
/// <para>THE ZOOM IS ALREADY HANDLED, and not by anything here. The rig — not the board — is what
/// the mod scales, so the device pose is already under the diorama scale and the controller grows
/// and shrinks with the hand it replaces, at every zoom level and hand size, for free. This class
/// contains no scale arithmetic at all, which is the point: any it contained would be a second
/// opinion about a number the rig already owns.</para>
///
/// <para>HIGHLIGHTING IS A PROPERTY BLOCK, not a material swap. A block writes <c>_Color</c> and
/// <c>_Ambient</c> on the renderer without touching the shared material, so nothing can leak into
/// the other hand's controller or survive the lesson — clearing it is <c>SetPropertyBlock(null)</c>
/// and there is no cloned material to forget.</para>
///
/// <para>A KEY THAT CANNOT LIGHT UP STILL GETS POINTED AT. The Valve Index's grip is a force
/// sensor in the handle that moves nothing, so its profile ships an ANCHOR and no mesh; the
/// generic fallback controller has no face buttons at all. When a key has no renderer, a small
/// pulsing marker is placed at the key's anchor instead, and when there is not even an anchor the
/// lesson still runs and still checks — it just names the key in words. Degrading was designed in
/// rather than discovered.</para>
/// </summary>
internal sealed class ControllerVisual
{
    /// <summary>Asset folder ids, matching the prefab folders under Assets/Bundle/Controllers.</summary>
    private const string Generic = "generic";

    /// <summary>A recognised controller: how to spot it, which model to show, what to call it,
    /// and whether its face buttons are a D-Pad rather than a diamond of four.</summary>
    private readonly struct Device
    {
        internal readonly string Id;      // log + panel identity
        internal readonly string Model;   // asset folder under Assets/Bundle/Controllers
        internal readonly string Label;   // what the player calls it
        internal readonly bool Dpad;      // the face inputs are a D-pad; word the steps that way

        internal Device(string id, string model, string label, bool dpad = false)
        {
            Id = id; Model = model; Label = label; Dpad = dpad;
        }
    }

    /// <summary>Substring → device, FIRST MATCH WINS, matched case-insensitively against
    /// <c>InputDevice.name</c>. The names are what OpenXR runtimes report ("Oculus Touch
    /// Controller OpenXR", "Index Controller OpenXR", …); the REAL name is logged once per
    /// session either way, so an unrecognised device names itself in the next hardware log
    /// rather than being guessed at from here.
    ///
    /// <para>THE STEAM FRAME IS RECOGNISED BUT WEARS THE GENERIC MODEL, and that is a decision
    /// rather than a gap. No openly-licensed model of its controllers exists anywhere: the
    /// webxr-input-profiles registry has no Valve entry beyond the Index, Valve's own Unity
    /// package ships the interaction profile
    /// (<c>/interaction_profiles/valve/frame_controller_valve</c>) and no art at all, and
    /// Valve's guidance is to fetch the model from the RUNTIME (<c>XR_EXT_render_model</c> /
    /// OpenVR's <c>IVRRenderModel</c>) instead of shipping one. Dressing it in a Meta controller
    /// because the two are shaped alike would show a Valve owner someone else's hardware —
    /// exactly what the upstream trademark note asks nobody to do. So it gets the neutral model,
    /// its own NAME, and wording that matches its real keys.</para></summary>
    private static readonly (string Needle, Device Device)[] DeviceTable =
    {
        ("quest touch plus", new Device("quest3", "quest3", "Quest 3")),
        ("touch plus", new Device("quest3", "quest3", "Quest 3")),
        ("meta quest", new Device("quest3", "quest3", "Quest")),
        ("oculus touch", new Device("quest3", "quest3", "Quest")),
        ("quest", new Device("quest3", "quest3", "Quest")),
        ("pico", new Device("pico4", "pico4", "Pico 4")),
        ("knuckles", new Device("index", "index", "Valve Index")),
        ("index", new Device("index", "index", "Valve Index")),
        ("frame_controller", new Device("steamframe", Generic, "Steam Frame", dpad: true)),
        ("steam frame", new Device("steamframe", Generic, "Steam Frame", dpad: true)),
        ("frame", new Device("steamframe", Generic, "Steam Frame", dpad: true)),
    };

    private const float PulseHz = 1.6f;
    private static readonly Color GlowLow = new(1.00f, 0.72f, 0.20f, 1f);
    private static readonly Color GlowHigh = new(1.00f, 0.96f, 0.72f, 1f);

    /// <summary>Radius of the marker used for a key with no mesh, in REAL metres — the model is
    /// under the rig scale, so this is multiplied by the hand's world scale like every other real
    /// length in the mod.</summary>
    private const float MarkerRadiusRealMeters = 0.010f;

    /// <summary>How long a hand-to-controller swap takes, in seconds (user ruling 2026-09-02:
    /// <i>"Wenn es zwischen Controller und Hand wechselt, sollte das mit einer kleinen Animation
    /// passieren statt einfach 'umzuploppen'."</i>). Short enough not to delay a step, long enough
    /// to read as a motion at 90 Hz — about twenty frames.</summary>
    private const float SwapSeconds = 0.22f;

    /// <summary>The point in the swap at which the hand mesh changes state, as a fraction of
    /// <see cref="_swap"/>. Not 0 and not 1: the two meshes overlap around the middle, so the
    /// controller is already half-grown when the hand goes and half-shrunk when it comes back, and
    /// neither disappearance happens against an empty hand.</summary>
    private const float HandCutFraction = 0.5f;

    private static Device? _resolved;
    private static bool _loggedDevice;

    /// <summary>One-shot: the FIRST completed swap of the session reports what the driver actually
    /// did — how many frames it took and the smallest and largest step it advanced by. A "small
    /// animation" that turns out to be a one-frame switch is a defect this project has shipped
    /// three times, and the only thing that catches it is measuring the DRIVER rather than
    /// asserting the intent.</summary>
    private static bool _swapProven;

    private readonly VRHand _hand;
    private GameObject? _model;
    private Transform? _marker;
    private readonly Dictionary<string, Renderer[]> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Transform> _anchors = new(StringComparer.Ordinal);
    private readonly List<Renderer> _hidden = new(32);
    private MaterialPropertyBlock? _block;
    private string? _lit;

    // ---- the swap ----------------------------------------------------------------------------
    // _swap is 0 when the hand is fully out and 1 when the controller is fully in; _swapTarget is
    // where it is heading. Reversing mid-swap is just a change of target, which is why a player who
    // presses through two steps faster than the animation cannot desynchronise it.
    private float _swap;
    private float _swapTarget;
    private bool _handHidden;
    private int _swapFrames;
    private float _swapMinStep = float.MaxValue;
    private float _swapMaxStep;
    private float _swapStartedAt = -1f;

    internal ControllerVisual(VRHand hand) => _hand = hand;

    internal bool IsShowing => _model != null;

    /// <summary>The device id whose model is being shown, for the log.</summary>
    internal static string DeviceId => _resolved?.Id ?? Generic;

    /// <summary>What the player calls their controller, for the panel.</summary>
    internal static string DeviceLabel => _resolved?.Label ?? "VR";

    /// <summary>True when the face inputs are a D-PAD, so the steps that talk about A/X and B/Y
    /// name what is actually under the player's thumb. The Steam Frame is the case that made this
    /// necessary: its four top inputs are a D-pad, and Valve's Touch-compatibility mapping sends
    /// A/X to the BOTTOM of it and B/Y to all three of the others.</summary>
    internal static bool HasDpad => _resolved?.Dpad ?? false;

    /// <summary>
    /// Identify the device WITHOUT showing anything. Since the 2026-09-02 ruling the models are
    /// only up while a step actually asks for a key press, but the very first card names the
    /// device in words ("your {0} controllers") — so the name has to be resolved before the first
    /// model ever appears. Idempotent; the log line still happens exactly once per session.
    /// </summary>
    internal static void EnsureResolved(VRHand? hand)
    {
        if (hand != null)
            ResolveDevice(hand);
    }

    /// <summary>
    /// Which recognised controller is connected. Falls back to a neutral generic device for
    /// anything the table does not know — and the Steam Frame reaches the generic MODEL by a
    /// different route: it IS recognised, but no openly licensed model of it exists (see the
    /// Controllers README and the device table above).
    /// </summary>
    private static Device ResolveDevice(VRHand hand)
    {
        if (_resolved.HasValue)
            return _resolved.Value;
        string name;
        try
        {
            InputDevice xr = InputDevices.GetDeviceAtXRNode(
                hand.Side == HandSide.Left ? XRNode.LeftHand : XRNode.RightHand);
            name = xr.isValid ? xr.name ?? string.Empty : string.Empty;
        }
        catch (Exception)
        {
            name = string.Empty;
        }
        string lower = name.ToLowerInvariant();
        var device = new Device(Generic, Generic, "VR");
        foreach ((string needle, Device candidate) in DeviceTable)
        {
            if (lower.Contains(needle))
            {
                device = candidate;
                break;
            }
        }
        if (!_loggedDevice)
        {
            _loggedDevice = true;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("Tutorial", $"Controls lesson: controller reported as '{name}' → "
                + $"'{device.Id}' ({device.Label}), showing the '{device.Model}' model"
                + (device.Model == Generic
                    ? " — no vendor model of this device is publicly licensed, so the neutral one "
                      + "is used; its keys are in the same places and every instruction names the "
                      + "key in words."
                    : ".")
                + (device.Dpad ? " Face inputs are a D-PAD; the steps are worded for it." : ""));
        }
        _resolved = device;
        return device;
    }

    /// <summary>True while a swap is part-way through, so the driver knows the model is still
    /// on its way in or out.</summary>
    internal bool IsSwapping => _model != null && !Mathf.Approximately(_swap, _swapTarget);

    /// <summary>
    /// Put the controller in this hand and take the hand mesh away — as a SWAP that takes
    /// <see cref="SwapSeconds"/>, not as a frame. Idempotent, and calling it while the reverse swap
    /// is running simply turns that swap round rather than rebuilding anything.
    /// </summary>
    internal void Show()
    {
        if (_model != null)
        {
            BeginSwap(1f);
            return;
        }
        Device device = ResolveDevice(_hand);
        string id = device.Model;
        string hand = _hand.Side == HandSide.Left ? "left" : "right";
        string path = $"Assets/Bundle/Controllers/{id}/Controller_{id}_{hand}.prefab";
        GameObject? prefab = WorldUI.WorldUIAssets.TryLoadPrefab(path);
        if (prefab == null && id != Generic)
        {
            VRLog.Warn("Tutorial", $"{path} is not in the bundle — falling back to the generic "
                + "controller. (A bundle older than the controls lesson will do this.)");
            path = $"Assets/Bundle/Controllers/{Generic}/Controller_{Generic}_{hand}.prefab";
            prefab = WorldUI.WorldUIAssets.TryLoadPrefab(path);
        }
        if (prefab == null)
        {
            VRLog.Warn("Tutorial", $"{path} is not in the bundle — the controls lesson runs "
                + "WITHOUT a controller model; every step still names its key in words and every "
                + "check still works.");
            return;
        }

        _model = UnityEngine.Object.Instantiate(prefab, _hand.transform, worldPositionStays: false);
        _model.name = $"GloomhavenVR.Controller_{id}_{hand}";
        _model.transform.localPosition = Vector3.zero;
        _model.transform.localRotation = Quaternion.identity;
        _model.transform.localScale = Vector3.zero;   // grown by the swap, never popped in

        foreach (Transform part in _model.transform)
        {
            var renderers = part.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
                _keys[part.name] = renderers;
            Transform anchor = part.Find("Anchor");
            if (anchor != null)
                _anchors[part.name] = anchor;
        }

        // A key may already have been asked for before the model existed: since the per-step
        // hand/controller ruling, a step can name a key and still show the HAND, which leaves
        // `_lit` set with nothing to light. Re-apply it against the renderers that have just
        // appeared — Highlight early-returns when `_lit` already equals the key, so without this
        // the key would silently never light on the next controller step.
        string? want = _lit;
        _lit = null;
        Highlight(want);

        _swap = 0f;
        _swapTarget = 0f;
        BeginSwap(1f);
    }

    /// <summary>
    /// Begin, or turn round, the swap. <paramref name="target"/> is 1 for controller-in and 0 for
    /// hand-back. The measurement fields are armed HERE rather than in <see cref="Tick"/>, so what
    /// gets reported is the run that actually happened.
    /// </summary>
    private void BeginSwap(float target)
    {
        if (Mathf.Approximately(_swapTarget, target))
            return;
        _swapTarget = target;
        _swapFrames = 0;
        _swapMinStep = float.MaxValue;
        _swapMaxStep = 0f;
        _swapStartedAt = Time.unscaledTime;
    }

    /// <summary>
    /// Start giving the hand back, over <see cref="SwapSeconds"/>. The model stays alive until the
    /// swap reaches zero, which is what makes this an animation rather than a delayed pop; use
    /// <see cref="Hide"/> when it has to be gone NOW (lesson end, scenario boundary, shutdown).
    /// </summary>
    internal void BeginHide()
    {
        if (_model == null)
            return;
        BeginSwap(0f);
    }

    /// <summary>Hand back, controller gone, every renderer restored, THIS FRAME. Safe to call
    /// twice. This is the teardown path; the animated one is <see cref="BeginHide"/>.</summary>
    internal void Hide()
    {
        Highlight(null);
        if (_marker != null)
        {
            UnityEngine.Object.Destroy(_marker.gameObject);
            _marker = null;
        }
        if (_model != null)
        {
            UnityEngine.Object.Destroy(_model);
            _model = null;
        }
        _keys.Clear();
        _anchors.Clear();
        _swap = 0f;
        _swapTarget = 0f;
        _swapStartedAt = -1f;
        ShowHand();
    }

    /// <summary>
    /// Advance the swap and paint it. The model scales between nothing and full size and the hand
    /// changes state at the crossover, so the two meshes overlap in the middle instead of one
    /// replacing the other in a frame.
    ///
    /// <para>IT IS THE CONTROLLER THAT IS SCALED, NEVER THE HAND, and that is not a detail:
    /// <c>handRoot.localScale</c> is the per-style hand SCALE that
    /// <c>HandVisuals.ApplyStyleScale</c> writes live from [Hands] GloveScale/PlateScale/
    /// ArcaneScale and compensates the attachment sockets against. Animating it would be a second
    /// writer of a hand-tuned config value, and would make the card fan and everything else
    /// socketed into the hand breathe with it. So the hand keeps its renderer switch and the
    /// motion belongs entirely to the object this class owns.</para>
    /// </summary>
    private void TickSwap()
    {
        if (_model == null)
            return;
        if (!Mathf.Approximately(_swap, _swapTarget))
        {
            float step = Mathf.Max(Time.unscaledDeltaTime, 0f) / Mathf.Max(SwapSeconds, 0.0001f);
            float before = _swap;
            _swap = Mathf.MoveTowards(_swap, _swapTarget, step);
            float moved = Mathf.Abs(_swap - before);
            _swapFrames++;
            if (moved < _swapMinStep)
                _swapMinStep = moved;
            if (moved > _swapMaxStep)
                _swapMaxStep = moved;
            if (Mathf.Approximately(_swap, _swapTarget))
                ReportSwapIfFirst();
        }

        // Smoothstep, so the two ends ease instead of starting and stopping at full speed.
        float eased = _swap * _swap * (3f - 2f * _swap);
        _model.transform.localScale = Vector3.one * eased;

        // The hand changes state at the crossover, and the test is on the CURRENT value rather
        // than on which way we are travelling — so turning a swap round mid-flight puts the hand
        // back in exactly the frame the crossover is re-crossed, with no extra bookkeeping.
        SetHandHidden(_swap >= HandCutFraction);

        if (_swap <= 0f && Mathf.Approximately(_swapTarget, 0f))
            Hide();   // the animated hide has finished; now the model can go
    }

    /// <summary>
    /// The one-shot that says what the DRIVER did, not what it was asked to do. This project has
    /// shipped a "continuous sweep" that was a one-frame switch three separate times, and the only
    /// thing that ever caught it was a line like this one.
    /// </summary>
    private void ReportSwapIfFirst()
    {
        if (_swapProven || _swapStartedAt < 0f)
            return;
        _swapProven = true;
        float seconds = Time.unscaledTime - _swapStartedAt;
        float min = _swapMinStep == float.MaxValue ? 0f : _swapMinStep;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("Tutorial", "Controls lesson hand/controller swap, measured on its FIRST run: "
            + $"{_swapFrames} frame(s) over {seconds:0.000} s travelling to {_swapTarget:0}, "
            + $"per-frame progress {min:0.000}..{_swapMaxStep:0.000} of 1. ONE frame here means it "
            + "is still a pop wearing an animation's clothes, whatever the intent was; the nominal "
            + $"length is {SwapSeconds:0.00} s.");
    }

    private void SetHandHidden(bool hidden)
    {
        if (hidden == _handHidden)
            return;
        _handHidden = hidden;
        if (hidden)
            HideHand();
        else
            ShowHand();
    }

    private void HideHand()
    {
        Transform? handRoot = _hand.Rig?.Root;
        if (handRoot == null)
            return;
        var renderers = handRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;   // already off — not ours to turn back on
            r.enabled = false;
            _hidden.Add(r);
        }
        _handHidden = true;
    }

    /// <summary>Restore only what WE switched off, by identity: another subsystem may have hidden
    /// a renderer for its own reasons while the lesson ran, and blanket-enabling everything under
    /// the hand would silently overrule it.</summary>
    private void ShowHand()
    {
        for (int i = 0; i < _hidden.Count; i++)
            if (_hidden[i] != null)
                _hidden[i].enabled = true;
        _hidden.Clear();
        _handHidden = false;
    }

    /// <summary>Light one key (a <see cref="ControllerKey"/> name), or clear with null.</summary>
    internal void Highlight(string? key)
    {
        if (_lit == key)
            return;
        if (_lit != null && _keys.TryGetValue(_lit, out Renderer[]? previous))
            for (int i = 0; i < previous.Length; i++)
                if (previous[i] != null)
                    previous[i].SetPropertyBlock(null);
        _lit = key;
        if (_marker != null)
            _marker.gameObject.SetActive(false);
        if (key == null)
            return;
        if (!_keys.ContainsKey(key) && _anchors.TryGetValue(key, out Transform? anchor))
            ShowMarkerAt(anchor);
    }

    /// <summary>Per-frame swap animation, then the pulse of whatever is currently lit. Cheap and
    /// allocation-free.</summary>
    internal void Tick()
    {
        TickSwap();
        if (_model == null || _lit == null)
            return;
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseHz * 2f * Mathf.PI);
        Color glow = Color.Lerp(GlowLow, GlowHigh, t);
        if (_keys.TryGetValue(_lit, out Renderer[]? renderers))
        {
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            _block.SetColor("_Color", glow);
            // BoardLit's baked key light would still shade the key; lifting the ambient floor to
            // 1 is what turns a tint into something that reads as LIT from every angle.
            _block.SetFloat("_Ambient", 1f);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null)
                    renderers[i].SetPropertyBlock(_block);
        }
        else if (_marker != null)
        {
            var mr = _marker.GetComponent<Renderer>();
            if (mr != null && mr.material != null)
                mr.material.color = glow;
            float scale = MarkerRadiusRealMeters * 2f * (0.85f + 0.3f * t);
            _marker.localScale = new Vector3(scale, scale, scale);
        }
    }

    private void ShowMarkerAt(Transform anchor)
    {
        if (_marker == null)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "GloomhavenVR.KeyMarker";
            UnityEngine.Object.Destroy(sphere.GetComponent<Collider>());
            var r = sphere.GetComponent<Renderer>();
            r.material = WorldUI.WorldUIAssets.CreateFlatMaterial(GlowHigh);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _marker = sphere.transform;
        }
        _marker.SetParent(anchor, worldPositionStays: false);
        _marker.localPosition = Vector3.zero;
        _marker.localRotation = Quaternion.identity;
        _marker.gameObject.SetActive(true);
    }
}

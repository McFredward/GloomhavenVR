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
/// <para>WHY THE DEVICE POSE AND NOT THE HAND ROOT. The model is parented to
/// <c>VRHand.transform</c>, which carries the raw <c>devicePosition/deviceRotation</c> — the same
/// grip pose the profile's authors modelled these meshes around. <c>_handRoot</c> below it exists
/// to hold the hand ART at a configurable visual offset, and it additionally carries the per-style
/// hand SCALE ([Hands] GloveScale/PlateScale/ArcaneScale). A controller is a real object of a real
/// size; hanging it off the art offset would move and resize a physical device to match a
/// stylistic choice about gloves.</para>
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

    /// <summary>Substring → device id, first match wins, matched case-insensitively against
    /// <c>InputDevice.name</c>. The names are what OpenXR runtimes report ("Oculus Touch
    /// Controller OpenXR", "Index Controller OpenXR", …); the REAL name is logged once per
    /// session either way, so an unrecognised device names itself in the next hardware log
    /// instead of being guessed at from here.</summary>
    private static readonly (string Needle, string Id)[] DeviceTable =
    {
        ("quest touch plus", "quest3"),
        ("touch plus", "quest3"),
        ("meta quest", "quest3"),
        ("oculus touch", "quest3"),
        ("quest", "quest3"),
        ("pico", "pico4"),
        ("index", "index"),
        ("knuckles", "index"),
    };

    private const float PulseHz = 1.6f;
    private static readonly Color GlowLow = new(1.00f, 0.72f, 0.20f, 1f);
    private static readonly Color GlowHigh = new(1.00f, 0.96f, 0.72f, 1f);

    /// <summary>Radius of the marker used for a key with no mesh, in REAL metres — the model is
    /// under the rig scale, so this is multiplied by the hand's world scale like every other real
    /// length in the mod.</summary>
    private const float MarkerRadiusRealMeters = 0.010f;

    private static string? _resolvedId;
    private static bool _loggedDevice;

    private readonly VRHand _hand;
    private GameObject? _model;
    private Transform? _marker;
    private readonly Dictionary<string, Renderer[]> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Transform> _anchors = new(StringComparer.Ordinal);
    private readonly List<Renderer> _hidden = new(32);
    private MaterialPropertyBlock? _block;
    private string? _lit;

    internal ControllerVisual(VRHand hand) => _hand = hand;

    internal bool IsShowing => _model != null;

    /// <summary>The device id whose model is being shown, for the panel's own text.</summary>
    internal static string DeviceId => _resolvedId ?? Generic;

    /// <summary>
    /// Which of the shipped models matches the connected controller. Falls back to the generic
    /// model for anything unrecognised — including Valve's Steam Frame, for which no openly
    /// licensed controller model exists (see the Controllers README).
    /// </summary>
    private static string ResolveDeviceId(VRHand hand)
    {
        if (_resolvedId != null)
            return _resolvedId;
        string name;
        try
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(
                hand.Side == HandSide.Left ? XRNode.LeftHand : XRNode.RightHand);
            name = device.isValid ? device.name ?? string.Empty : string.Empty;
        }
        catch (Exception)
        {
            name = string.Empty;
        }
        string lower = name.ToLowerInvariant();
        string id = Generic;
        foreach ((string needle, string mapped) in DeviceTable)
        {
            if (lower.Contains(needle))
            {
                id = mapped;
                break;
            }
        }
        if (!_loggedDevice)
        {
            _loggedDevice = true;
            VRLog.Info("Tutorial", $"Controls lesson: controller reported as '{name}' → showing "
                + $"the '{id}' model"
                + (id == Generic
                    ? " (no bespoke model for this device; the generic one has the same keys in "
                      + "the same places, and every instruction still names the key)."
                    : "."));
        }
        _resolvedId = id;
        return id;
    }

    /// <summary>Put the controller in this hand and take the hand mesh away. Idempotent.</summary>
    internal void Show()
    {
        if (_model != null)
            return;
        string id = ResolveDeviceId(_hand);
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
        _model.transform.localScale = Vector3.one;

        foreach (Transform part in _model.transform)
        {
            var renderers = part.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
                _keys[part.name] = renderers;
            Transform anchor = part.Find("Anchor");
            if (anchor != null)
                _anchors[part.name] = anchor;
        }

        HideHand();
    }

    /// <summary>Hand back, controller gone, every renderer restored. Safe to call twice.</summary>
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
        // Restore only what WE switched off, by identity: another subsystem may have hidden a
        // renderer for its own reasons while the lesson ran, and blanket-enabling everything
        // under the hand would silently overrule it.
        for (int i = 0; i < _hidden.Count; i++)
            if (_hidden[i] != null)
                _hidden[i].enabled = true;
        _hidden.Clear();
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

    /// <summary>Per-frame pulse of whatever is currently lit. Cheap and allocation-free.</summary>
    internal void Tick()
    {
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

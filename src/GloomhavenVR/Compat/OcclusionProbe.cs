using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// SUPERSEDED (opt-in fallback only) — LINE-OF-SIGHT OCCLUSION PROBE for shaders that cannot be
/// depth-corrected via material properties. RETIRED per user mandate: the binary hide/show (with
/// its ~1s hysteresis delay) toggles objects on/off, which is rejected — <see cref="DepthShaderSwap"/>
/// now fixes the same shaders as PURE RENDER STATE (permanent instance-material swap to
/// depth-testing replacements; walls occlude naturally, no toggling). The probe stays compiled as
/// an opt-in fallback: config default is now FALSE, and while disabled it discovers nothing and
/// restores anything it ever hid. The <see cref="WorldUI.ActorBars"/> integration was removed —
/// bars now depth-test their materials instead (see ActorBars).
///
/// The census (see <see cref="GlowOcclusion"/>) proved the remaining bleed-through elements use
/// game shaders with HARDCODED depth behavior: they expose no _ZTest/_ZWrite material properties
/// (census `ztest=-`), so the property-based enforcement can never fix them — fire/torch particles
/// (VFX/ParticleMasterUnlitAdd_Shd), cloud bits (SimpleParticleAlphaDFade), GPU bits, distortion
/// particles, the hex waypoint path, X-ray floor tiles (Amp_Basic_Unseen), moths (WingFlap) and
/// the selected-hex decal (OmniDecal_Shd) all draw through walls by shader design (fine for the
/// flat top-down camera, wrong in VR). Walls DO write depth (opaque figures occlude correctly);
/// the bleeders simply skip the test.
///
/// FIX: shader-agnostic binary hide. Every ~0.2 s we <see cref="Physics.Linecast"/> from the HMD
/// head position to each targeted renderer's bounds center (plus the bounds TOP for tall targets —
/// visible if ANY probe is clear); when world geometry stands in between for 2 consecutive probes
/// the renderer is disabled, and re-enabled after 2 consecutive clear probes (hysteresis kills
/// flicker at wall edges). Unity <see cref="Light"/>s parented under — or within ~1.5 world units
/// of — a targeted renderer are probed the same way, because shadowless lights illuminate through
/// walls. (World-space health bars are no longer probed here — <see cref="WorldUI.ActorBars"/>
/// depth-tests its graphics' materials instead.)
///
/// TARGET DISCOVERY rides <see cref="GlowOcclusion"/>'s existing sweep schedule (the
/// <see cref="GlowOcclusion.PostSweep"/> hook — ~2s/5s/10s/20s/40s after scene load, then every
/// ~15s) so no second census runs; only the cheap 0.2 s probe ticks are our own
/// (<c>GloomhavenVR.OcclusionProbe</c> driver, DontDestroyOnLoad). Probing is round-robin capped
/// at <see cref="MaxProbesPerTick"/> targets per tick, ≤2 linecasts each — well under 0.5 ms.
///
/// SAFETY: config-gated (<c>OcclusionProbe</c>, default ON; shader set tunable via
/// <c>OcclusionProbeShaders</c> without rebuild); strict no-op when VR isn't running; never
/// touches the mod layer or "GloomhavenVR*" objects (as target OR as occluder — the mask excludes
/// the mod layer); never claims renderers/lights the game itself disabled (ownership is only ever
/// a transition WE caused); everything we hid is restored on Uninstall, scene change, and live
/// config-off. Occluder hits are logged (collider name + layer) on the first transitions so the
/// real wall layer can be confirmed on hardware and the mask refined.
/// </summary>
internal static class OcclusionProbe
{
    private const string Name = "OcclusionProbe";
    private const string DriverName = "GloomhavenVR.OcclusionProbe";

    /// <summary>Probe cadence.</summary>
    internal const float ProbeIntervalSeconds = 0.2f;

    /// <summary>Round-robin budget: at most this many targets probed per 0.2 s tick.</summary>
    private const int MaxProbesPerTick = 40;

    /// <summary>Hysteresis: consecutive blocked probes required to hide / clear probes to show.</summary>
    private const int HideThreshold = 2;
    private const int ShowThreshold = 2;

    /// <summary>First N hide/show transitions are logged verbosely (with occluder details on hides).</summary>
    private const int VerboseTransitionLogMax = 10;

    /// <summary>
    /// A hit this close to the far END of the line is the target's own mounting surface (e.g. the
    /// wall a torch hangs ON), not an occluder BETWEEN head and target — treat as clear.
    /// World units (walls are meter-scale scene geometry; the mod scales the rig, not the world).
    /// </summary>
    private const float EndClearanceWU = 0.10f;

    /// <summary>Probe the bounds TOP too when the target is taller than this (world units).</summary>
    private const float TallBoundsHeightWU = 0.4f;

    /// <summary>A Light within this distance of a targeted renderer is treated as its glow source.</summary>
    private const float LightAssociateDistanceWU = 1.5f;

    /// <summary>All mod-created GameObjects share this name prefix (rig, hands, sky, drivers).</summary>
    private const string ModNamePrefix = "GloomhavenVR";

    /// <summary>Census-proven depth-ignoring shaders (hardcoded behavior, no _ZTest property).</summary>
    private const string DefaultShaders =
        "VFX/ParticleMasterUnlitAdd_Shd,SimpleParticleAlphaDFade,VFX/GPU_Bits_Shd,"
        + "KriptoFX/RFX4/DistortionParticles,VFX/HexWaypointPath_Shd,Amp_Basic_Unseen,"
        + "VFX/WingFlap_Shd,OmniDecal_Shd";

    private static ConfigFile? _configFile;
    private static ConfigEntry<bool>? _enabled;
    private static ConfigEntry<string>? _shaderConfig;

    private static bool _installed;
    private static ProbeDriver? _driver;
    private static bool _firstFailureLogged;

    // Occluder mask: everything EXCEPT the mod layer (sky/laser/hands), TransparentFX(1),
    // Ignore Raycast(2) and UI(5). Deliberately broad — the first-hide logs report the actual
    // occluder layer so the mask can be narrowed once hardware confirms where walls live.
    private static bool _maskResolved;
    private static int _occluderMask;

    // Shader set, re-parsed when the config string changes (tunable without rebuild).
    private static readonly HashSet<string> _shaderSet = new(StringComparer.Ordinal);
    private static string? _parsedShaderString;

    // ---- targets (rebuilt on each GlowOcclusion sweep; hysteresis state carried over) ---------
    private sealed class RendererTarget
    {
        public Renderer R = null!;
        public int Blocked;
        public int Clear;
        public bool HiddenByUs;
        /// <summary>Probe points cached while visible — particle bounds collapse once disabled.</summary>
        public Vector3 Center;
        public Vector3 Top;
        public bool Tall;
    }

    private sealed class LightTarget
    {
        public Light L = null!;
        public int Blocked;
        public int Clear;
        public bool HiddenByUs;
    }

    private static Dictionary<int, RendererTarget> _rendererTargets = [];
    private static Dictionary<int, LightTarget> _lightTargets = [];
    private static readonly List<RendererTarget> _rendererList = [];
    private static readonly List<LightTarget> _lightList = [];
    private static int _cursor;                    // round-robin position over renderers+lights

    // Bounded transition logging, then counts (reported in the sweep heartbeat).
    private static int _verboseTransitions;
    private static int _hides;
    private static int _shows;

    /// <summary>
    /// True when the probe is installed and config-enabled. Default is now OFF — the probe is a
    /// fallback superseded by <see cref="DepthShaderSwap"/>.
    /// </summary>
    internal static bool Enabled => _installed && (_enabled?.Value ?? false);

    // ------------------------------------------------------------------ install / uninstall

    /// <summary>
    /// Bind config, hook target discovery onto <see cref="GlowOcclusion.PostSweep"/> and start the
    /// 0.2 s probe driver. Idempotent; strict no-op when VR isn't running.
    /// </summary>
    public static void Install()
    {
        if (_installed || !VRSession.IsRunning)
            return;
        EnsureConfig();
        SceneManager.sceneLoaded += OnSceneLoaded;
        GlowOcclusion.PostSweep += OnSweep;
        EnsureDriver();
        _installed = true;
        VRLog.Info(Name, $"installed: probe every {ProbeIntervalSeconds:0.0#}s, "
            + $"hysteresis {HideThreshold} blocked to hide / {ShowThreshold} clear to show, "
            + $"round-robin ≤{MaxProbesPerTick}/tick, gate={( _enabled?.Value ?? true ? "ON" : "OFF")}.");
    }

    /// <summary>Unhook everything, destroy the driver, and restore all hidden renderers/lights.</summary>
    public static void Uninstall()
    {
        if (!_installed)
            return;
        _installed = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GlowOcclusion.PostSweep -= OnSweep;

        if (_driver != null)
        {
            try { UnityEngine.Object.Destroy(_driver.gameObject); }
            catch { /* scene teardown already got it */ }
            _driver = null;
        }

        RestoreAll("shutdown");
    }

    private static void EnsureConfig()
    {
        if (_enabled != null)
            return;
        // Standalone module config file (dev.gloomhavenvr.probe.cfg) — no edit to Plugin.cs needed.
        _configFile = ModuleConfig.Create("probe");
        _enabled = _configFile.Bind("Compat", "OcclusionProbe", false,
            "LEGACY FALLBACK (default off — superseded by DepthShaderSwap): line-of-sight "
            + "occlusion probe that HIDES fire/torch particles, hex decals, X-ray floor tiles, "
            + "moths and similar depth-ignoring VFX (plus their point lights) whenever a wall "
            + "stands between the player's head and them. Binary hide with flicker hysteresis "
            + "(~1s reaction delay). Only enable if the render-state DepthShaderSwap fix is "
            + "disabled or misbehaves on your hardware.");
        _shaderConfig = _configFile.Bind("Compat", "OcclusionProbeShaders", DefaultShaders,
            "Comma-separated shader names whose renderers the occlusion probe manages. Seeded with "
            + "the census-proven depth-ignoring set; tunable without rebuild.");
    }

    private static void EnsureDriver()
    {
        if (_driver != null)
            return;
        var go = new GameObject(DriverName); // mod prefix ⇒ auto-excluded from every scan
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<ProbeDriver>();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // All instance IDs are stale; restore whatever survived teardown and start clean. The next
        // sweep (~2s post-load) rediscovers targets once the procedural map exists.
        RestoreAll("scene change");
    }

    /// <summary>Re-enable everything we disabled and drop all target state.</summary>
    private static void RestoreAll(string reason)
    {
        int restored = 0;
        foreach (RendererTarget t in _rendererList)
        {
            if (t.HiddenByUs && t.R != null)
            {
                try { t.R.enabled = true; restored++; }
                catch { /* destroyed under us */ }
            }
        }
        foreach (LightTarget t in _lightList)
        {
            if (t.HiddenByUs && t.L != null)
            {
                try { t.L.enabled = true; restored++; }
                catch { /* destroyed under us */ }
            }
        }
        if (restored > 0)
            VRLog.Info(Name, $"restored {restored} hidden renderer(s)/light(s) ({reason}).");

        _rendererTargets.Clear();
        _lightTargets.Clear();
        _rendererList.Clear();
        _lightList.Clear();
        _cursor = 0;
    }

    // ------------------------------------------------------------------ target discovery (sweep)

    /// <summary>
    /// Rebuild the target set — runs on <see cref="GlowOcclusion"/>'s sweep cadence via
    /// <see cref="GlowOcclusion.PostSweep"/> (no second census). Hysteresis/ownership state is
    /// carried across rebuilds by renderer/light instance ID; targets that vanished are restored
    /// if we hid them. Never throws into the game.
    /// </summary>
    private static void OnSweep()
    {
        if (!_installed || !VRSession.IsRunning)
            return;

        if (!Enabled)
        {
            // Superseded-by-default: while OFF (the new default), don't waste sweeps discovering
            // targets; just release anything still held from a live config flip.
            if (_rendererList.Count > 0 || _lightList.Count > 0)
                RestoreAll("config off");
            return;
        }

        try
        {
            ParseShaderSet();

            // --- renderers -------------------------------------------------------------------
            var newRenderers = new Dictionary<int, RendererTarget>();
            _rendererList.Clear();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || r.gameObject.layer == VRLayers.ModLayer || IsModOwned(r.transform))
                    continue;

                Material m;
                try { m = r.sharedMaterial; }
                catch { continue; }
                if (m == null || m.shader == null || !_shaderSet.Contains(m.shader.name))
                    continue;

                int id = r.GetInstanceID();
                if (_rendererTargets.TryGetValue(id, out RendererTarget? known))
                {
                    newRenderers[id] = known;   // keep hysteresis + ownership
                    _rendererList.Add(known);
                    continue;
                }
                if (!r.enabled)
                    continue; // the GAME disabled it — never ours to manage
                var t = new RendererTarget { R = r };
                RefreshProbePoints(t);
                newRenderers[id] = t;
                _rendererList.Add(t);
            }

            // Targets that dropped out (destroyed / GO deactivated / shader set changed): release
            // ownership — restore if we were the ones hiding them.
            foreach (KeyValuePair<int, RendererTarget> kv in _rendererTargets)
            {
                if (newRenderers.ContainsKey(kv.Key))
                    continue;
                RendererTarget t = kv.Value;
                if (t.HiddenByUs && t.R != null)
                {
                    try { t.R.enabled = true; }
                    catch { /* destroyed under us */ }
                }
            }
            _rendererTargets = newRenderers;

            // --- lights: parented under, or within ~1.5 wu of, a targeted renderer ------------
            var newLights = new Dictionary<int, LightTarget>();
            _lightList.Clear();
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (l == null || l.gameObject.layer == VRLayers.ModLayer || IsModOwned(l.transform))
                    continue;
                if (l.type == LightType.Directional)
                    continue; // scene sun — never a torch glow
                if (!IsLightAssociated(l))
                    continue;

                int id = l.GetInstanceID();
                if (_lightTargets.TryGetValue(id, out LightTarget? known))
                {
                    newLights[id] = known;
                    _lightList.Add(known);
                    continue;
                }
                if (!l.enabled)
                    continue; // game-disabled — not ours
                var t = new LightTarget { L = l };
                newLights[id] = t;
                _lightList.Add(t);
            }
            foreach (KeyValuePair<int, LightTarget> kv in _lightTargets)
            {
                if (newLights.ContainsKey(kv.Key))
                    continue;
                LightTarget t = kv.Value;
                if (t.HiddenByUs && t.L != null)
                {
                    try { t.L.enabled = true; }
                    catch { /* destroyed under us */ }
                }
            }
            _lightTargets = newLights;

            // Heartbeat (mirrors the GlowOcclusion sweep heartbeat style).
            int hiddenR = 0;
            foreach (RendererTarget t in _rendererList)
                if (t.HiddenByUs) hiddenR++;
            int hiddenL = 0;
            foreach (LightTarget t in _lightList)
                if (t.HiddenByUs) hiddenL++;
            VRLog.Debug(Name,
                $"probe heartbeat: targets={_rendererList.Count} lights={_lightList.Count} "
                + $"hidden={hiddenR} hiddenLights={hiddenL} hides+={_hides} shows+={_shows} "
                + $"gate={(Enabled ? "ON" : "OFF")}.");
            _hides = 0;
            _shows = 0;
        }
        catch (Exception e)
        {
            LogFirstFailure($"target sweep threw: {e.Message}");
        }
    }

    /// <summary>True if a targeted renderer sits in the light's parent chain or within ~1.5 wu.</summary>
    private static bool IsLightAssociated(Light l)
    {
        // Parent-chain check: any ancestor (or self) hosting a targeted renderer.
        Transform? t = l.transform;
        for (int depth = 0; t != null && depth < 64; t = t.parent, depth++)
        {
            foreach (RendererTarget rt in _rendererList)
            {
                if (rt.R != null && rt.R.transform == t)
                    return true;
            }
        }

        // Distance check: near any targeted renderer's cached bounds center.
        Vector3 pos = l.transform.position;
        float maxSq = LightAssociateDistanceWU * LightAssociateDistanceWU;
        foreach (RendererTarget rt in _rendererList)
        {
            if ((rt.Center - pos).sqrMagnitude <= maxSq)
                return true;
        }
        return false;
    }

    private static void ParseShaderSet()
    {
        string raw = _shaderConfig?.Value ?? DefaultShaders;
        if (ReferenceEquals(raw, _parsedShaderString) || raw == _parsedShaderString)
            return;
        _parsedShaderString = raw;
        _shaderSet.Clear();
        foreach (string part in raw.Split(','))
        {
            string name = part.Trim();
            if (name.Length > 0)
                _shaderSet.Add(name);
        }
        VRLog.Info(Name, $"target shader set: {_shaderSet.Count} shader name(s).");
    }

    /// <summary>Cache the probe points while the renderer is visible (bounds collapse when disabled).</summary>
    private static void RefreshProbePoints(RendererTarget t)
    {
        try
        {
            Bounds b = t.R.bounds;
            t.Center = b.center;
            t.Top = new Vector3(b.center.x, b.max.y, b.center.z);
            t.Tall = b.size.y > TallBoundsHeightWU;
        }
        catch { /* keep previous cached points */ }
    }

    // ------------------------------------------------------------------ probing (0.2 s driver)

    /// <summary>0.2 s probe ticker (DontDestroyOnLoad, mod-prefixed GO). No per-frame work.</summary>
    private sealed class ProbeDriver : MonoBehaviour
    {
        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            var wait = new WaitForSecondsRealtime(ProbeIntervalSeconds);
            while (true)
            {
                yield return wait;
                ProbeTick();
            }
        }
    }

    /// <summary>
    /// One probe tick: round-robin over up to <see cref="MaxProbesPerTick"/> targets
    /// (renderers, then lights, shared cursor). Never throws into the game.
    /// </summary>
    private static void ProbeTick()
    {
        if (!_installed || !VRSession.IsRunning)
            return;

        try
        {
            if (!Enabled)
            {
                // Live config-off: put everything back once, then idle until re-enabled.
                if (_rendererList.Count > 0 || _lightList.Count > 0)
                    RestoreAll("config off");
                return;
            }

            int total = _rendererList.Count + _lightList.Count;
            if (total == 0)
                return;

            Camera? head = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
            if (head == null)
                return;
            Vector3 headPos = head.transform.position;

            int budget = Math.Min(MaxProbesPerTick, total);
            for (int i = 0; i < budget; i++)
            {
                int idx = _cursor % total;
                _cursor = (_cursor + 1) % total;
                if (idx < _rendererList.Count)
                    ProbeRenderer(_rendererList[idx], headPos);
                else
                    ProbeLight(_lightList[idx - _rendererList.Count], headPos);
            }
        }
        catch (Exception e)
        {
            LogFirstFailure($"probe tick threw: {e.Message}");
        }
    }

    private static void ProbeRenderer(RendererTarget t, Vector3 headPos)
    {
        Renderer r = t.R;
        if (r == null || !r.gameObject.activeInHierarchy)
            return; // sweep will restore/drop it
        if (!t.HiddenByUs && !r.enabled)
        {
            // The game disabled it since discovery — not ours; forget any pending hysteresis.
            t.Blocked = 0;
            t.Clear = 0;
            return;
        }

        if (!t.HiddenByUs)
            RefreshProbePoints(t); // keep points fresh while visible (VFX move/grow)

        Transform self = r.transform;
        bool blocked = LinecastBlocked(headPos, t.Center, self, out RaycastHit hit);
        if (blocked && t.Tall && !LinecastBlocked(headPos, t.Top, self, out _))
            blocked = false; // visible if ANY probe is clear

        if (blocked)
        {
            t.Clear = 0;
            if (++t.Blocked >= HideThreshold && !t.HiddenByUs)
            {
                if (!r.enabled)
                    return; // game got there first — never claim its transition
                r.enabled = false;
                t.HiddenByUs = true;
                LogHide(PathOf(r.transform), hit);
            }
        }
        else
        {
            t.Blocked = 0;
            if (++t.Clear >= ShowThreshold && t.HiddenByUs)
            {
                r.enabled = true;
                t.HiddenByUs = false;
                LogShow(PathOf(r.transform));
            }
        }
    }

    private static void ProbeLight(LightTarget t, Vector3 headPos)
    {
        Light l = t.L;
        if (l == null || !l.gameObject.activeInHierarchy)
            return;
        if (!t.HiddenByUs && !l.enabled)
        {
            t.Blocked = 0;
            t.Clear = 0;
            return;
        }

        bool blocked = LinecastBlocked(headPos, l.transform.position, l.transform, out RaycastHit hit);
        if (blocked)
        {
            t.Clear = 0;
            if (++t.Blocked >= HideThreshold && !t.HiddenByUs)
            {
                if (!l.enabled)
                    return;
                l.enabled = false;
                t.HiddenByUs = true;
                LogHide(PathOf(l.transform) + " (Light)", hit);
            }
        }
        else
        {
            t.Blocked = 0;
            if (++t.Clear >= ShowThreshold && t.HiddenByUs)
            {
                l.enabled = true;
                t.HiddenByUs = false;
                LogShow(PathOf(l.transform) + " (Light)");
            }
        }
    }

    // ------------------------------------------------------------------ shared probe primitive

    /// <summary>
    /// True when world geometry stands BETWEEN <paramref name="from"/> (head) and
    /// <paramref name="to"/> (target). Ignores triggers, the mod layer, TransparentFX/UI,
    /// hits inside <paramref name="self"/>'s own hierarchy, and hits within
    /// <see cref="EndClearanceWU"/> of the far end (the target's own mounting surface).
    /// </summary>
    internal static bool LinecastBlocked(Vector3 from, Vector3 to, Transform? self, out RaycastHit hit)
    {
        hit = default;
        float length = (to - from).magnitude;
        if (length < 1e-4f)
            return false;
        if (!Physics.Linecast(from, to, out hit, OccluderMask, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.distance >= length - EndClearanceWU)
            return false; // occluder surface essentially AT the target — its own mount, not a wall
        Transform? c = hit.collider != null ? hit.collider.transform : null;
        for (int depth = 0; c != null && depth < 64; c = c.parent, depth++)
        {
            if (c == self)
                return false; // hit the target's own collider hierarchy
        }
        return true;
    }

    private static int OccluderMask
    {
        get
        {
            if (!_maskResolved)
            {
                _maskResolved = true;
                _occluderMask = ~(VRLayers.ModLayerMask | (1 << 1) | (1 << 2) | (1 << 5));
                VRLog.Debug(Name, $"occluder mask resolved: 0x{_occluderMask:X8} "
                    + "(everything except mod layer, TransparentFX, Ignore Raycast, UI).");
            }
            return _occluderMask;
        }
    }

    // ------------------------------------------------------------------ bounded transition log

    /// <summary>
    /// First ~10 transitions verbose (hides include occluder collider name + layer — the evidence
    /// for the real wall layer), then counts only (sweep heartbeat).
    /// </summary>
    internal static void LogHide(string label, in RaycastHit hit)
    {
        _hides++;
        if (_verboseTransitions >= VerboseTransitionLogMax)
            return;
        _verboseTransitions++;
        string occluder = hit.collider != null
            ? $"'{hit.collider.name}' layer={hit.collider.gameObject.layer} "
              + $"'{LayerMask.LayerToName(hit.collider.gameObject.layer)}'"
            : "<none>";
        VRLog.Info(Name, $"occlusion probe: HID '{label}' (occluder {occluder} between head and target)."
            + (_verboseTransitions == VerboseTransitionLogMax
                ? " (verbose transition log cap reached — counting only from here.)" : string.Empty));
    }

    /// <summary>Show counterpart of <see cref="LogHide"/> — same shared verbose budget.</summary>
    internal static void LogShow(string label)
    {
        _shows++;
        if (_verboseTransitions >= VerboseTransitionLogMax)
            return;
        _verboseTransitions++;
        VRLog.Info(Name, $"occlusion probe: SHOWED '{label}' (line of sight clear)."
            + (_verboseTransitions == VerboseTransitionLogMax
                ? " (verbose transition log cap reached — counting only from here.)" : string.Empty));
    }

    /// <summary>'parent/name' label, mirroring the census example-path format.</summary>
    private static string PathOf(Transform t)
    {
        Transform? parent = t.parent;
        return parent != null ? $"{parent.name}/{t.name}" : t.name;
    }

    /// <summary>True if any transform up the chain is a mod object ("GloomhavenVR*" name).</summary>
    private static bool IsModOwned(Transform? t)
    {
        for (int depth = 0; t != null && depth < 64; t = t.parent, depth++)
        {
            if (t.name.StartsWith(ModNamePrefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void LogFirstFailure(string message)
    {
        if (_firstFailureLogged)
            return;
        _firstFailureLogged = true;
        VRLog.Warn(Name, message);
    }
}

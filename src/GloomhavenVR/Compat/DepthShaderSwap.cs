using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 final form — PURE RENDER-STATE depth correction for the census-proven shaders that
/// HARDCODE "draw on top" (no _ZTest property, intended for the flat top-down camera). The user
/// mandate: nothing may be toggled on/off any more — the line-of-sight probe
/// (<see cref="OcclusionProbe"/>, hides with ~1s delay) is retired to an opt-in fallback. Here
/// every offending renderer stays permanently ENABLED but its per-renderer instance materials
/// (<c>r.materials</c> — shared assets are never mutated) are swapped to a depth-testing
/// replacement, so walls occlude them naturally, frame-exact, with zero toggling. Only the
/// mod's sky depth-reset (mod layer) may see through — never touched here.
///
/// SWAP TABLE (hardware census evidence):
///  - <c>Amp_Basic_Unseen</c> (undiscovered-area floor hexes, queue 3000) → the game's own
///    <c>Amp_Basic</c> shader (same family, matching serialized properties → lit look survives),
///    resolved by scanning already-loaded materials. Fallback: bundled Overlay (alpha blend).
///  - <c>OmniDecal_Shd</c> (selected-hex ring/decals, queue 4000), <c>VFX/HexWaypointPath_Shd</c>,
///    <c>SimpleParticleAlphaDFade</c> (clouds) → bundled <c>GloomhavenVR/Overlay</c>, alpha blend
///    (SrcAlpha/OneMinusSrcAlpha), ZTest LEqual, ZWrite off, Cull off, original queue kept.
///  - <c>VFX/ParticleMasterUnlitAdd_Shd</c> (fire/torch/candle flames+glow) and
///    <c>VFX/GPU_Bits_Shd</c> → bundled Overlay ADDITIVE (One/One), ZTest LEqual, original queue.
///    Overlay samples vertex color, so per-particle color/alpha-over-lifetime survives; texture-
///    sheet animation rides UV0 and survives too.
///  - <c>VFX/WingFlap_Shd</c> (moths) → Overlay alpha blend. KNOWN TRADE-OFF: the wing-flap is
///    vertex animation inside the original shader and is LOST (static moth sprites) —
///    depth-correctness wins per the user's rule.
///  - <c>KriptoFX/RFX4/DistortionParticles</c> is deliberately LEFT UNTOUCHED: it is a grab-pass
///    distortion — through a wall it only distorts the wall's own pixels (barely perceptible),
///    while swapping it to Overlay would draw its noise texture as visible geometry and look
///    strictly WORSE.
///
/// TORCH LIGHT SHADOWS (config <c>TorchLightShadows</c>): the radiating LIGHT of fires/candles
/// passes through walls because those Lights cast no shadows. Every active non-directional Light
/// parented under — or within ~1.5 world units of — a renderer swapped by the additive rule gets
/// <see cref="LightShadows.Hard"/> so the light physically stops at walls. No toggling, no delay.
///
/// Runs on <see cref="GlowOcclusion.PostSweep"/> (the existing sweep cadence — no second census
/// schedule). Idempotent across sweeps: a swapped renderer's materials no longer match the swap
/// table, and instance IDs are tracked besides. Config-gated (<c>DepthShaderSwap</c>, default ON,
/// module config dev.gloomhavenvr.depthswap.cfg), strict no-op when VR isn't running, reversible:
/// original shared material arrays and light shadow modes are snapshotted and restored on
/// live config-off and <see cref="Uninstall"/>. Never throws into the game. Also gates the
/// <see cref="WorldUI.ActorBars"/> health-bar depth-test (<see cref="BarsDepthTest"/>).
/// </summary>
internal static class DepthShaderSwap
{
    private const string Name = "DepthShaderSwap";
    private const int VerboseSwapLogMax = 30;

    /// <summary>A shadowless Light within this distance of an additive-swapped renderer (flame
    /// glow) is treated as a torch/candle/fire light.</summary>
    private const float LightAssociateDistanceWU = 1.5f;

    /// <summary>All mod-created GameObjects share this name prefix (rig, hands, sky, drivers).</summary>
    private const string ModNamePrefix = "GloomhavenVR";

    // Overlay shader material properties (see unity/GloomhavenVR.Assets/.../Overlay.shader).
    private static readonly int MainTexProp = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapProp = Shader.PropertyToID("_BaseMap");
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly int TintColorProp = Shader.PropertyToID("_TintColor");
    private static readonly int ZTestProp = Shader.PropertyToID("_ZTest");
    private static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");
    private static readonly int CullProp = Shader.PropertyToID("_Cull");
    private static readonly int SrcBlendProp = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendProp = Shader.PropertyToID("_DstBlend");

    // UnityEngine.Rendering.BlendMode / CompareFunction values (ints to keep intent obvious).
    private const int BlendOne = 1;                // BlendMode.One
    private const int BlendSrcAlpha = 5;           // BlendMode.SrcAlpha
    private const int BlendOneMinusSrcAlpha = 10;  // BlendMode.OneMinusSrcAlpha
    private const int ZTestLEqual = 4;             // CompareFunction.LessEqual

    private enum SwapKind
    {
        /// <summary>Same-family lit shader swap (Amp_Basic_Unseen → Amp_Basic).</summary>
        AmpBasic,
        /// <summary>Bundled Overlay, alpha blend (SrcAlpha / OneMinusSrcAlpha).</summary>
        OverlayAlpha,
        /// <summary>Bundled Overlay, additive blend (One / One) — flames/glow/bits.</summary>
        OverlayAdditive,
    }

    /// <summary>Census-proven depth-ignoring shaders → depth-testing replacement family.
    /// KriptoFX/RFX4/DistortionParticles is deliberately absent (see class doc).</summary>
    private static readonly Dictionary<string, SwapKind> SwapTable = new(StringComparer.Ordinal)
    {
        ["Amp_Basic_Unseen"] = SwapKind.AmpBasic,
        ["OmniDecal_Shd"] = SwapKind.OverlayAlpha,
        ["VFX/HexWaypointPath_Shd"] = SwapKind.OverlayAlpha,
        ["SimpleParticleAlphaDFade"] = SwapKind.OverlayAlpha,
        // Moths: wing-flap vertex animation lives in the original shader and is lost by the
        // swap (static sprites). Accepted trade-off — depth-correctness wins per user mandate.
        ["VFX/WingFlap_Shd"] = SwapKind.OverlayAlpha,
        ["VFX/ParticleMasterUnlitAdd_Shd"] = SwapKind.OverlayAdditive,
        ["VFX/GPU_Bits_Shd"] = SwapKind.OverlayAdditive,
    };

    private static ConfigFile? _configFile;
    private static ConfigEntry<bool>? _enabled;
    private static ConfigEntry<bool>? _torchShadows;

    private static bool _installed;
    private static bool _firstFailureLogged;
    private static int _verboseSwapLogs;

    // Shader resolution (both lazy; retried every sweep until found).
    private static Shader? _overlayShader;
    private static bool _overlayLogged;
    private static bool _overlayMissLogged;
    private static Shader? _ampBasicShader;
    private static bool _ampBasicLogged;
    private static bool _ampBasicMissLogged;

    // Outline exclusion types (same exclusions as GlowOcclusion's enforcement).
    private static bool _typesResolved;
    private static Type? _outlinableType;
    private static Type? _outlineWrapperType;

    // ---- bookkeeping for idempotence + restore ------------------------------------------------
    private sealed class SwappedRenderer
    {
        public Renderer R = null!;
        public int Id;
        public Material[] OrigShared = null!;
        /// <summary>True when any material got the ADDITIVE rule (flame/glow) — light anchor.</summary>
        public bool Additive;
    }

    private sealed class ShadowedLight
    {
        public Light L = null!;
        public int Id;
        public LightShadows Orig;
    }

    private static readonly List<SwappedRenderer> _swapped = [];
    private static readonly HashSet<int> _swappedIds = [];
    private static readonly List<ShadowedLight> _lights = [];
    private static readonly HashSet<int> _lightIds = [];

    /// <summary>
    /// Gate read by <see cref="WorldUI.ActorBars"/>: health bars force
    /// <c>unity_GUIZTestMode</c>=LEqual on their graphics while this module is on.
    /// </summary>
    internal static bool BarsDepthTest => _installed && (_enabled?.Value ?? true);

    // ------------------------------------------------------------------ install / uninstall

    /// <summary>
    /// Bind config and ride <see cref="GlowOcclusion.PostSweep"/> (exactly like the retired
    /// <see cref="OcclusionProbe"/> did). Idempotent; strict no-op when VR isn't running.
    /// </summary>
    public static void Install()
    {
        if (_installed || !VRSession.IsRunning)
            return;
        EnsureConfig();
        SceneManager.sceneLoaded += OnSceneLoaded;
        GlowOcclusion.PostSweep += OnSweep;
        _installed = true;
        VRLog.Info(Name, $"installed: render-state depth swap for {SwapTable.Count} shader(s), "
            + $"gate={(_enabled?.Value ?? true ? "ON" : "OFF")}, "
            + $"torchShadows={(_torchShadows?.Value ?? true ? "ON" : "OFF")}.");
    }

    /// <summary>Unhook and restore every swapped renderer / shadowed light (hot-reload safety).</summary>
    public static void Uninstall()
    {
        if (!_installed)
            return;
        _installed = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GlowOcclusion.PostSweep -= OnSweep;
        RestoreAll("shutdown");
    }

    private static void EnsureConfig()
    {
        if (_enabled != null)
            return;
        // Standalone module config file (dev.gloomhavenvr.depthswap.cfg) — module-config pattern.
        _configFile = ModuleConfig.Create("depthswap");
        _enabled = _configFile.Bind("Compat", "DepthShaderSwap", true,
            "Render-state depth correction: swaps the game's hardcoded draw-on-top shaders "
            + "(fire/torch/candle flames+glow, cloud bits, undiscovered-area floor hexes, hex "
            + "selection ring, waypoint path, moths) to depth-testing replacements so walls "
            + "occlude them naturally in VR — objects stay permanently enabled, nothing is "
            + "toggled. Also makes world-space health bars depth-test. Trade-offs: moth wing-flap "
            + "animation becomes static; the unseen-floor dim tint is approximated. Disable to "
            + "restore the original shaders (elements then bleed through walls again unless the "
            + "legacy OcclusionProbe fallback is enabled).");
        _torchShadows = _configFile.Bind("Compat", "TorchLightShadows", true,
            "Give torch/candle/fire point lights hard shadows so their LIGHT physically stops at "
            + "walls (shadowless lights illuminate through geometry). Cheap hard shadows; disable "
            + "if performance suffers.");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Stale-entry hygiene: objects that died with the old scene can't be restored anyway.
        _swapped.RemoveAll(e => e.R == null);
        _lights.RemoveAll(e => e.L == null);
        _swappedIds.Clear();
        _lightIds.Clear();
        foreach (SwappedRenderer e in _swapped)
            _swappedIds.Add(e.Id);
        foreach (ShadowedLight e in _lights)
            _lightIds.Add(e.Id);
    }

    /// <summary>Restore all swapped renderers and shadowed lights, drop all tracking.</summary>
    private static void RestoreAll(string reason)
    {
        int restored = 0;
        foreach (SwappedRenderer e in _swapped)
        {
            if (e.R != null && e.OrigShared != null)
            {
                try { e.R.sharedMaterials = e.OrigShared; restored++; }
                catch { /* destroyed under us */ }
            }
        }
        foreach (ShadowedLight e in _lights)
        {
            if (e.L != null)
            {
                try { e.L.shadows = e.Orig; restored++; }
                catch { /* destroyed under us */ }
            }
        }
        if (restored > 0)
            VRLog.Info(Name, $"restored {restored} swapped renderer(s)/shadowed light(s) ({reason}).");
        _swapped.Clear();
        _swappedIds.Clear();
        _lights.Clear();
        _lightIds.Clear();
    }

    // ------------------------------------------------------------------ sweep

    /// <summary>
    /// One pass on the sweep cadence: shader swaps first, then torch light shadows.
    /// Never throws into the game.
    /// </summary>
    private static void OnSweep()
    {
        if (!_installed || !VRSession.IsRunning)
            return;

        try
        {
            if (!(_enabled?.Value ?? true))
            {
                // Live config-off: put everything back once, then idle until re-enabled.
                if (_swapped.Count > 0 || _lights.Count > 0)
                    RestoreAll("config off");
                return;
            }

            EnsureTypes();
            Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
            ResolveShaders(all);

            int swappedNow = SweepSwaps(all);

            int newLights = 0;
            if (_torchShadows?.Value ?? true)
            {
                newLights = SweepLights();
            }
            else if (_lights.Count > 0)
            {
                // TorchLightShadows flipped off live: give the lights their original mode back.
                foreach (ShadowedLight e in _lights)
                {
                    if (e.L != null)
                    {
                        try { e.L.shadows = e.Orig; }
                        catch { /* destroyed under us */ }
                    }
                }
                VRLog.Info(Name, $"torch shadows: restored {_lights.Count} light(s) (config off).");
                _lights.Clear();
                _lightIds.Clear();
            }

            if (swappedNow > 0 || newLights > 0)
            {
                VRLog.Info(Name, $"depth swap sweep: swapped {swappedNow} renderer(s) "
                    + $"({_swapped.Count} tracked), hardened {newLights} torch light(s) "
                    + $"({_lights.Count} tracked).");
            }
            VRLog.Debug(Name, $"depth swap heartbeat: tracked={_swapped.Count} "
                + $"lights={_lights.Count} overlay={(_overlayShader != null ? "ok" : "MISSING")} "
                + $"ampBasic={(_ampBasicShader != null ? "ok" : "unresolved")}.");
        }
        catch (Exception e)
        {
            LogFirstFailure($"sweep threw: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ shader resolution

    private static void ResolveShaders(Renderer[] all)
    {
        // Bundled Overlay: NOT discoverable via Shader.Find until something loads it — load it
        // explicitly from whichever loaded bundle holds it (PlayTray.OverlayShader() pattern,
        // replicated here so Compat stays self-contained). No bundle rebuild needed.
        if (_overlayShader == null)
        {
            _overlayShader = Shader.Find("GloomhavenVR/Overlay");
            if (_overlayShader == null)
            {
                foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b == null)
                        continue;
                    try
                    {
                        Shader s = b.LoadAsset<Shader>("Assets/Bundle/Table/Overlay.shader");
                        if (s != null) { _overlayShader = s; break; }
                    }
                    catch { /* bundle without the asset */ }
                }
            }
            if (_overlayShader != null && !_overlayLogged)
            {
                _overlayLogged = true;
                VRLog.Info(Name, "bundled 'GloomhavenVR/Overlay' shader resolved for depth swaps.");
            }
            else if (_overlayShader == null && !_overlayMissLogged)
            {
                _overlayMissLogged = true;
                VRLog.Warn(Name, "bundled 'GloomhavenVR/Overlay' shader NOT found yet — overlay "
                    + "swaps deferred until a bundle carrying it loads (retried every sweep).");
            }
        }

        // Amp_Basic: the game's own lit family shader — resolve from any already-loaded material
        // (the census sees the whole world each sweep; the discovered map always contains
        // discovered Amp_Basic tiles next to the Amp_Basic_Unseen ones).
        if (_ampBasicShader == null)
        {
            foreach (Renderer r in all)
            {
                if (r == null)
                    continue;
                Material[] shared;
                try { shared = r.sharedMaterials; }
                catch { continue; }
                foreach (Material m in shared)
                {
                    if (m != null && m.shader != null && m.shader.name == "Amp_Basic")
                    {
                        _ampBasicShader = m.shader;
                        break;
                    }
                }
                if (_ampBasicShader != null)
                    break;
            }
            if (_ampBasicShader != null && !_ampBasicLogged)
            {
                _ampBasicLogged = true;
                VRLog.Info(Name, "game shader 'Amp_Basic' resolved from a loaded material — "
                    + "unseen floor hexes swap to the lit family shader.");
            }
            else if (_ampBasicShader == null && !_ampBasicMissLogged)
            {
                _ampBasicMissLogged = true;
                VRLog.Info(Name, "game shader 'Amp_Basic' not seen in loaded materials yet — "
                    + "unseen floor hexes fall back to the bundled Overlay until it appears.");
            }
        }
    }

    // ------------------------------------------------------------------ renderer swaps

    /// <summary>
    /// Swap every non-excluded world renderer whose shared materials match the table.
    /// Idempotent: swapped materials no longer match the table AND instance IDs are tracked.
    /// Returns the number of renderers swapped this sweep.
    /// </summary>
    private static int SweepSwaps(Renderer[] all)
    {
        int swappedNow = 0;
        foreach (Renderer r in all)
        {
            if (r == null)
                continue;
            int id = r.GetInstanceID();
            if (_swappedIds.Contains(id))
                continue;

            Material[] shared;
            try { shared = r.sharedMaterials; }
            catch { continue; }
            if (!MatchesTable(shared))
                continue;
            if (IsExcluded(r))
                continue;

            // Snapshot ORIGINAL shared assets for restore, then work on per-renderer INSTANCES
            // (r.materials — never mutate shared assets; other renderers keep the stock look
            // until their own swap).
            Material[] instances;
            try { instances = r.materials; }
            catch { continue; }

            bool changed = false;
            bool additive = false;
            foreach (Material m in instances)
            {
                if (m == null || m.shader == null)
                    continue;
                if (!SwapTable.TryGetValue(m.shader.name, out SwapKind kind))
                    continue;
                string oldShader = m.shader.name;
                bool ok;
                string blendLabel;
                switch (kind)
                {
                    case SwapKind.AmpBasic:
                        ok = SwapToAmpBasic(m, out blendLabel);
                        break;
                    case SwapKind.OverlayAdditive:
                        ok = SwapToOverlay(m, additiveBlend: true);
                        blendLabel = "additive One/One";
                        break;
                    default:
                        ok = SwapToOverlay(m, additiveBlend: false);
                        blendLabel = "alpha SrcAlpha/OneMinusSrcAlpha";
                        break;
                }
                if (!ok)
                    continue; // replacement shader not resolved yet — retried next sweep
                changed = true;
                if (kind == SwapKind.OverlayAdditive)
                    additive = true;
                LogSwapBounded(r, oldShader, m.shader != null ? m.shader.name : "<null>", blendLabel);
            }

            if (changed)
            {
                _swapped.Add(new SwappedRenderer { R = r, Id = id, OrigShared = shared, Additive = additive });
                _swappedIds.Add(id);
                swappedNow++;
            }
            else
            {
                // Nothing swappable yet (e.g. Overlay bundle not loaded) — drop the instance
                // materials we just created so the renderer keeps its shared assets.
                try { r.sharedMaterials = shared; }
                catch { /* destroyed under us */ }
            }
        }
        return swappedNow;
    }

    private static bool MatchesTable(Material[] shared)
    {
        foreach (Material m in shared)
        {
            if (m != null && m.shader != null && SwapTable.ContainsKey(m.shader.name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Swap an instance material to the bundled Overlay, preserving look: original main texture
    /// (also probing _BaseMap), tint (_TintColor/_Color), and render queue. Depth state:
    /// ZTest LEqual, ZWrite off, Cull off. Vertex color passes through the Overlay shader, so
    /// per-particle color/alpha-over-lifetime survives.
    /// </summary>
    private static bool SwapToOverlay(Material m, bool additiveBlend)
    {
        if (_overlayShader == null)
            return false;

        int queue = m.renderQueue;
        Texture? tex = null;
        Color tint = Color.white;
        try
        {
            if (m.HasProperty(MainTexProp))
                tex = m.GetTexture(MainTexProp);
            if (tex == null && m.HasProperty(BaseMapProp))
                tex = m.GetTexture(BaseMapProp);
            if (tex == null)
                tex = m.mainTexture;
            if (m.HasProperty(TintColorProp))
                tint = m.GetColor(TintColorProp);
            else if (m.HasProperty(ColorProp))
                tint = m.GetColor(ColorProp);
        }
        catch { /* keep defaults — white tint, no texture override */ }

        m.shader = _overlayShader;
        m.SetFloat(ZTestProp, ZTestLEqual);
        m.SetFloat(ZWriteProp, 0f);
        m.SetFloat(CullProp, 0f);
        m.SetFloat(SrcBlendProp, additiveBlend ? BlendOne : BlendSrcAlpha);
        m.SetFloat(DstBlendProp, additiveBlend ? BlendOne : BlendOneMinusSrcAlpha);
        if (tex != null)
            m.SetTexture(MainTexProp, tex);
        m.SetColor(ColorProp, tint);
        m.renderQueue = queue; // keep the original ordering (3000 VFX / 4000 decals)
        return true;
    }

    /// <summary>
    /// Swap Amp_Basic_Unseen → the game's Amp_Basic (same family — serialized properties like
    /// _MainTex carry over, so the lit tile look survives). Falls back to Overlay alpha with the
    /// original texture when Amp_Basic is unresolved. The "unseen" dimming that lived in the
    /// original shader is approximated with a dark tint.
    /// </summary>
    private static bool SwapToAmpBasic(Material m, out string blendLabel)
    {
        if (_ampBasicShader == null)
        {
            blendLabel = "alpha SrcAlpha/OneMinusSrcAlpha (Amp_Basic fallback)";
            return SwapToOverlay(m, additiveBlend: false);
        }

        m.shader = _ampBasicShader;
        // Approximate the lost "unseen" dim: moderately dark gray tint. TUNABLE — losing the
        // exact dimmed look is acceptable per mandate (tiles must simply not shine through walls).
        try
        {
            if (m.HasProperty(ColorProp))
                m.SetColor(ColorProp, new Color(0.6f, 0.6f, 0.6f, 1f));
        }
        catch { /* tint is cosmetic only */ }
        m.renderQueue = -1; // shader default — native opaque/lit depth path, not the old 3000
        blendLabel = "lit opaque (Amp_Basic)";
        return true;
    }

    /// <summary>First ~30 swaps logged verbosely, then counts only (sweep summary).</summary>
    private static void LogSwapBounded(Renderer r, string oldShader, string newShader, string blend)
    {
        if (_verboseSwapLogs >= VerboseSwapLogMax)
            return;
        _verboseSwapLogs++;
        VRLog.Info(Name, $"depth swap: '{PathOf(r.transform)}' shader '{oldShader}' -> '{newShader}' ({blend})."
            + (_verboseSwapLogs == VerboseSwapLogMax
                ? " (verbose swap log cap reached — counting only from here.)" : string.Empty));
    }

    // ------------------------------------------------------------------ torch light shadows

    /// <summary>
    /// Give torch/candle/fire lights hard shadows: every active non-directional shadowless Light
    /// whose parent chain contains an additive-swapped renderer, or whose position is within
    /// ~1.5 world units of one, gets <see cref="LightShadows.Hard"/> (cheap) — the light then
    /// physically stops at walls. Originals remembered for restore; logged once per light.
    /// Returns the number of lights hardened this sweep.
    /// </summary>
    private static int SweepLights()
    {
        // Anchors: additive-swapped renderers (flames/glow) still alive this sweep.
        var anchors = new List<(Transform t, Vector3 center)>();
        foreach (SwappedRenderer e in _swapped)
        {
            if (!e.Additive || e.R == null)
                continue;
            Vector3 center;
            try { center = e.R.bounds.center; }
            catch { center = e.R.transform.position; }
            anchors.Add((e.R.transform, center));
        }
        if (anchors.Count == 0)
            return 0;

        int hardened = 0;
        float maxSq = LightAssociateDistanceWU * LightAssociateDistanceWU;
        foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            if (l == null || l.type == LightType.Directional)
                continue; // scene sun — never a torch glow
            if (l.gameObject.layer == VRLayers.ModLayer || IsModOwned(l.transform))
                continue;
            if (l.shadows != LightShadows.None)
                continue; // already shadowing — walls stop it on their own
            int id = l.GetInstanceID();
            if (_lightIds.Contains(id))
                continue;
            if (!IsLightAssociated(l, anchors, maxSq))
                continue;

            _lights.Add(new ShadowedLight { L = l, Id = id, Orig = l.shadows });
            _lightIds.Add(id);
            try { l.shadows = LightShadows.Hard; }
            catch
            {
                _lights.RemoveAt(_lights.Count - 1);
                _lightIds.Remove(id);
                continue;
            }
            hardened++;
            if (_verboseSwapLogs < VerboseSwapLogMax)
            {
                _verboseSwapLogs++;
                VRLog.Info(Name, $"torch shadows: '{PathOf(l.transform)}' (Light) shadows None -> Hard "
                    + "(light now stops at walls).");
            }
        }
        return hardened;
    }

    /// <summary>True if an additive-swapped renderer sits in the light's parent chain or nearby.</summary>
    private static bool IsLightAssociated(Light l, List<(Transform t, Vector3 center)> anchors, float maxSq)
    {
        Transform? p = l.transform;
        for (int depth = 0; p != null && depth < 64; p = p.parent, depth++)
        {
            foreach ((Transform t, Vector3 _) in anchors)
            {
                if (t == p)
                    return true;
            }
        }
        Vector3 pos = l.transform.position;
        foreach ((Transform _, Vector3 center) in anchors)
        {
            if ((center - pos).sqrMagnitude <= maxSq)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ exclusions (mirror GlowOcclusion)

    /// <summary>
    /// Same exclusions as <see cref="GlowOcclusion"/>'s enforcement: mod layer, mod-owned
    /// ("GloomhavenVR*") parents, Canvas graphics, EPOOutline Outlinable/OutlineWrapper.
    /// Conservative: an exclusion check that throws excludes the renderer.
    /// </summary>
    private static bool IsExcluded(Renderer r)
    {
        if (r.gameObject.layer == VRLayers.ModLayer)
            return true;
        try
        {
            if (IsModOwned(r.transform))
                return true;
            if (r.GetComponentInParent<Canvas>() != null)
                return true;
            if (_outlinableType != null && r.GetComponentInParent(_outlinableType) != null)
                return true;
            if (_outlineWrapperType != null && r.GetComponentInParent(_outlineWrapperType) != null)
                return true;
        }
        catch (Exception e)
        {
            LogFirstFailure($"exclusion check threw for '{r.name}' — excluding to be safe: {e.Message}");
            return true;
        }
        return false;
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

    private static void EnsureTypes()
    {
        if (_typesResolved)
            return;
        _typesResolved = true;
        _outlinableType = ResolveType("EPOOutline.Outlinable") ?? ResolveType("Outlinable");
        _outlineWrapperType = ResolveType("OutlineWrapper");
    }

    /// <summary>Resolve a type by (assembly-qualified or plain) name across loaded assemblies.</summary>
    private static Type? ResolveType(string name)
    {
        Type? type = AccessTools.TypeByName(name);
        if (type != null)
            return type;
        foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? t = asm.GetType(name, throwOnError: false);
            if (t != null)
                return t;
        }
        return null;
    }

    /// <summary>'parent/name' label, mirroring the census example-path format.</summary>
    private static string PathOf(Transform t)
    {
        Transform? parent = t.parent;
        return parent != null ? $"{parent.name}/{t.name}" : t.name;
    }

    private static void LogFirstFailure(string message)
    {
        if (_firstFailureLogged)
            return;
        _firstFailureLogged = true;
        VRLog.Warn(Name, message);
    }
}

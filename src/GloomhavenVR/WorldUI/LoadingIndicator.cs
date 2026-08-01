using System;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// VR loading indicator (user request: "loading screens read as a total VR freeze").
///
/// WHY: the game has NO synchronous scene loads — <c>SceneController.LoadSceneCoroutine</c>
/// polls <c>LoadSceneAsync</c>/<c>UnloadSceneAsync</c> per frame and the user's Player.log
/// proves dozens of frames render during a scenario load. VR still LOOKS frozen because
/// nothing visible changes: the flat loading UI lives on Screen-Space-CAMERA canvases whose
/// UICamera is destroyed mid-transition (CanvasManager rebinds on sceneLoaded), so the
/// loading screen reaches neither the HMD eye textures nor the FlatScreen capture RT — the
/// HMD shows only the menu rig's black void + hands, motionless.
///
/// This component makes the wait READ as live, in the game's own visual language:
/// <list type="bullet">
/// <item>While <c>SceneController.Instance.IsLoading || ScenarioIsLoading</c> (public
///   properties, plain per-tick poll — zero Harmony patches), two stacked mod-layer quads
///   float ~1.5 m ahead of the CURRENT rig head camera showing the flat loading screen's
///   REAL spinner art: the <c>LoadingScreen.m_LoadingIconBase</c>/<c>m_LoadingIconOverlay</c>
///   sprites read once via AccessTools, animated exactly like the game's
///   <c>AnimateIcon()</c> — a discrete <c>m_IconSpinSpeed</c>-degree Z step every
///   <c>m_IconUpdateSpeed</c> seconds (the stepped look IS the visual identity) with the
///   overlay alpha-pulsed by <c>m_IconGlowSpeed</c> between <c>m_IconOverlayMinAlpha</c>
///   and 1. Reflection/sprite failure falls back to a procedural ring + arc-gradient
///   overlay — never a crash, logged once.</item>
/// <item>"Only the spinner on black": while active, <see cref="FlatScreenSuppressed"/>
///   gates <c>FlatScreen.WantVisible</c> to false, so the 2D quad (hints/progress/half-dead
///   menu composite) hides and the Menu2D head camera's black void clear carries the
///   backdrop. The gate is recomputed every tick, so the screen returns through its normal
///   Show() path the moment loading ends. Hands stay visible (mod layer).</item>
/// <item>Smaller hitches (part 2): while loading, <c>Application.backgroundLoadingPriority</c>
///   is dropped to <c>Low</c> — Unity's async-integration time slice per frame shrinks, so
///   the many 25–100 ms integration hitches get smaller at the cost of a somewhat longer
///   load. The game never sets the value and only polls <c>isDone</c>, so nothing depends
///   on load timing. The EXACT prior value is captured before the flip and restored when
///   loading ends (and defensively on <see cref="Shutdown"/>).</item>
/// </list>
///
/// The residual big single frames (per-transition GC.Collect spike, procgen
/// WaitForCompletion frames, the one-time boot activation frame) are in-process
/// unsplittable — the spinner briefly freezes there too; accepted (see commit).
///
/// LIFETIME: the quads parent under the head camera (identity local rotation = always
/// facing; a per-tick world-pose copy would lag the tracked head by a frame and swim).
/// The rig can be torn down/rebuilt MID-transition, which destroys the parented quads —
/// every tick re-resolves <c>VRRigDriver.HeadCamera</c> and rebuilds on Unity-null, so the
/// indicator survives any number of rig cycles. Purely local presentation (MP: nothing on
/// the wire); every game-object mutation is mod-owned; [WorldUI] LoadingIndicator=false
/// disables the whole feature (visuals, gate and priority flip).
/// </summary>
internal sealed class LoadingIndicator
{
    // ---- placement (head-local) --------------------------------------------------------
    private const float DistanceMeters = 1.5f;
    private const float BelowEyeMeters = 0.18f;
    private const float SizeMeters = 0.25f;
    /// <summary>Overlay sits this far in FRONT of the base quad (toward the head) so the
    /// transparent queue's distance sort always draws it over the base.</summary>
    private const float OverlayLiftMeters = 0.005f;

    // ---- animation fallbacks (used only when the LoadingScreen tuning fields are
    // unreadable/degenerate; chosen to read like the game's stepped spinner) -------------
    private const float FallbackSpinDegrees = 6f;
    private const float FallbackStepSeconds = 0.025f;
    private const float FallbackGlowStep = 0.02f;
    private const float FallbackMinAlpha = 0.25f;
    /// <summary>Max animation steps consumed per frame — a residual spike frame drops its
    /// backlog instead of fast-forwarding the spinner in one visible jump.</summary>
    private const int MaxStepsPerFrame = 8;

    /// <summary>One spinner layer's acquired art + geometry (base or overlay).</summary>
    private sealed class LayerArt
    {
        public Texture? Tex;              // null = procedural fallback texture in FallbackTex
        public Rect Uv = new(0f, 0f, 1f, 1f); // normalized sub-rect (sprite atlas support)
        public Color Tint = Color.white;
        public float Width = 1f;          // authored aspect (uGUI rect), normalized below
        public float Height = 1f;
        public float SizeRatio = 1f;      // this layer's size relative to the BASE layer
    }

    /// <summary>
    /// True while the loading indicator owns the HMD picture — <c>FlatScreen.WantVisible</c>
    /// returns false so ONLY spinner + hands show on the void. Recomputed every tick;
    /// false the instant loading ends or the feature is toggled off (gate self-restores).
    /// </summary>
    internal static bool FlatScreenSuppressed { get; private set; }

    private GameObject? _root;
    private Transform? _baseQuad;
    private Transform? _overlayQuad;
    private Material? _baseMaterial;
    private Material? _overlayMaterial;
    private Texture2D? _fallbackBaseTex;
    private Texture2D? _fallbackOverlayTex;

    // Art + tuning, read ONCE from the live LoadingScreen (cached across rig rebuilds).
    private LayerArt? _baseArt;
    private LayerArt? _overlayArt;
    private float _spinDegrees = FallbackSpinDegrees;
    private float _stepSeconds = FallbackStepSeconds;
    private float _glowStep = FallbackGlowStep;
    private float _minAlpha = FallbackMinAlpha;
    private bool _artFailedLogged;

    // AnimateIcon replica state.
    private float _accum;
    private float _overlayAlpha;
    private bool _increaseAlpha;

    // Background-loading-priority flip (part 2).
    private bool _prioritySet;
    private UnityEngine.ThreadPriority _priorPriority;

    private bool _shownLogged;

    public void Tick()
    {
        bool loading = VRSession.IsRunning && IsGameLoading();

        // Part 2 runs whenever the feature is on and a load is in flight — flipped and
        // restored on the edges only, one log line each (existing VRLog style).
        TickLoadPriority(loading && WorldUIConfig.LoadingIndicator.Value);

        bool want = loading && WorldUIConfig.LoadingIndicator.Value;
        FlatScreenSuppressed = want;

        if (!want)
        {
            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
                VRLog.Info("WorldUI", "Loading indicator hidden (loading ended) — flat screen released.");
            }
            _shownLogged = false;
            return;
        }

        // Re-resolve the head EVERY tick: the menu/scenario rig is torn down and rebuilt
        // mid-transition, killing both the old camera and (via parenting) our quads.
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
        {
            if (_root != null)
                _root.SetActive(false); // no camera = nothing renders anyway; wait for the rebuild
            return;
        }

        if (_root == null)
            BuildVisual();
        if (_root == null)
            return; // hard build failure already logged (once)

        if (_root.transform.parent != head.transform)
        {
            _root.transform.SetParent(head.transform, worldPositionStays: false);
            _root.transform.localPosition = new Vector3(0f, -BelowEyeMeters, DistanceMeters);
            _root.transform.localRotation = Quaternion.identity; // child of the head = always facing
            _root.transform.localScale = Vector3.one;
        }
        if (!_root.activeSelf)
            _root.SetActive(true);
        if (!_shownLogged)
        {
            _shownLogged = true;
            VRLog.Info("WorldUI", $"Loading indicator shown: game spinner {DistanceMeters:0.0} m ahead of head " +
                                  $"'{head.name}' (step {_spinDegrees:0.#}° / {_stepSeconds:0.###}s, glow {_glowStep:0.###}, " +
                                  $"min alpha {_minAlpha:0.##}) — flat screen suppressed for the load.");
        }

        Animate();
    }

    public void Shutdown()
    {
        // Defensive restore: never leave the process with a lowered loading priority.
        if (_prioritySet)
        {
            Application.backgroundLoadingPriority = _priorPriority;
            _prioritySet = false;
            VRLog.Info("WorldUI", $"Loading indicator shutdown: backgroundLoadingPriority restored to {_priorPriority}.");
        }
        FlatScreenSuppressed = false;
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
        _baseQuad = null;
        _overlayQuad = null;
        DestroyObj(ref _baseMaterial);
        DestroyObj(ref _overlayMaterial);
        DestroyObj(ref _fallbackBaseTex);
        DestroyObj(ref _fallbackOverlayTex);
        _baseArt = null;
        _overlayArt = null;
        _shownLogged = false;
    }

    private static void DestroyObj<T>(ref T? obj) where T : UnityEngine.Object
    {
        if (obj != null)
        {
            UnityEngine.Object.Destroy(obj);
            obj = null;
        }
    }

    /// <summary>The game is in a loading transition (public state, no patches needed).</summary>
    private static bool IsGameLoading()
    {
        SceneController sc = SceneController.Instance;
        return sc != null && (sc.IsLoading || sc.ScenarioIsLoading);
    }

    // ---- part 2: async-integration slice shrink ------------------------------------------

    private void TickLoadPriority(bool loading)
    {
        if (loading && !_prioritySet)
        {
            _priorPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.Low;
            _prioritySet = true;
            VRLog.Info("WorldUI", $"Loading started: backgroundLoadingPriority {_priorPriority} → Low " +
                                  "(smaller per-frame async-integration slices — smaller hitches, slightly longer load).");
        }
        else if (!loading && _prioritySet)
        {
            Application.backgroundLoadingPriority = _priorPriority;
            _prioritySet = false;
            VRLog.Info("WorldUI", $"Loading ended: backgroundLoadingPriority restored to {_priorPriority}.");
        }
    }

    // ---- visuals -------------------------------------------------------------------------

    private void BuildVisual()
    {
        ResolveArt();
        if (_baseArt == null || _overlayArt == null)
            return; // ResolveArt always produces SOMETHING; null only on a logged hard failure

        _root = new GameObject("GloomhavenVR.LoadingIndicator");
        // Parented under the (DontDestroyOnLoad) rig, but the flag also covers the brief
        // unparented window during a rebuild — Single-mode scene loads must never take it.
        UnityEngine.Object.DontDestroyOnLoad(_root);

        // A mid-transition rig teardown destroys the parented quads but NOT these materials
        // — drop the orphans before creating the rebuild's set (no per-rebuild leak).
        DestroyObj(ref _baseMaterial);
        DestroyObj(ref _overlayMaterial);
        _baseMaterial = CreateLayerMaterial(_baseArt, _fallbackBaseTex);
        _overlayMaterial = CreateLayerMaterial(_overlayArt, _fallbackOverlayTex);
        _baseQuad = CreateQuad("Base", _baseMaterial, _baseArt, 0f);
        _overlayQuad = CreateQuad("Overlay", _overlayMaterial, _overlayArt, -OverlayLiftMeters);

        // Match LoadingScreen.OnEnable: overlay starts at min alpha, rising.
        _overlayAlpha = _minAlpha;
        _increaseAlpha = true;
        _accum = 0f;
        ApplyOverlayAlpha();

        VRLayers.Apply(_root); // mod layer: only the rig head camera renders it (CAMERA-POLICY §2)
    }

    private Material CreateLayerMaterial(LayerArt art, Texture2D? fallbackTex)
    {
        // Same shader family as WorldUIAssets.CreateFlatMaterial, but alpha-blended on
        // purpose: the spinner art lives in the sprite's alpha channel, and Sprites/Default
        // ships shipped-verified (FlatScreen glass uses it) with a _Color tint the glow
        // pulse can drive.
        Shader? shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("UI/Default")
                         ?? Shader.Find("Hidden/InternalErrorShader");
        var material = new Material(shader)
        {
            mainTexture = art.Tex != null ? art.Tex : fallbackTex,
            color = art.Tint,
            // Sub-rect via UV transform: a Unity Quad spans UV 0..1, so scale/offset map it
            // onto the sprite's atlas rect exactly.
            mainTextureScale = new Vector2(art.Uv.width, art.Uv.height),
            mainTextureOffset = new Vector2(art.Uv.x, art.Uv.y),
        };
        return material;
    }

    private Transform CreateQuad(string name, Material material, LayerArt art, float zOffset)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "GloomhavenVR.LoadingIndicator." + name;
        UnityEngine.Object.Destroy(quad.GetComponent<Collider>()); // visual only — never a ray/poke target
        quad.GetComponent<Renderer>().sharedMaterial = material;
        Transform t = quad.transform;
        t.SetParent(_root!.transform, worldPositionStays: false);
        t.localPosition = new Vector3(0f, 0f, zOffset); // -z = toward the head (quad front faces the camera)
        t.localRotation = Quaternion.identity;
        // Normalize the authored aspect so the larger side spans SizeMeters × SizeRatio.
        float max = Mathf.Max(art.Width, art.Height);
        float size = SizeMeters * art.SizeRatio;
        t.localScale = new Vector3(size * art.Width / max, size * art.Height / max, 1f);
        return t;
    }

    /// <summary>
    /// Read the REAL spinner art + tuning off the live <c>LoadingScreen</c> once
    /// (AccessTools field access on the private serialized fields). Any failure — missing
    /// instance, tight-packed sprite (textureRect throws), renamed field after a game
    /// update — falls back to a procedural ring/arc so the indicator NEVER crashes or
    /// goes blank; logged once at Warn.
    /// </summary>
    private void ResolveArt()
    {
        if (_baseArt != null && _overlayArt != null)
            return;
        try
        {
            LoadingScreen? ls = SceneController.Instance != null ? SceneController.Instance.LoadingScreenInstance : null;
            if (ls != null)
            {
                var baseGo = AccessTools.Field(typeof(LoadingScreen), "m_LoadingIconBase")?.GetValue(ls) as GameObject;
                var overlayGo = AccessTools.Field(typeof(LoadingScreen), "m_LoadingIconOverlay")?.GetValue(ls) as GameObject;
                LayerArt? baseArt = ReadLayer(baseGo, baseGo);
                LayerArt? overlayArt = ReadLayer(overlayGo, baseGo);
                if (baseArt != null && overlayArt != null)
                {
                    _baseArt = baseArt;
                    _overlayArt = overlayArt;
                    _spinDegrees = ReadTuning(ls, "m_IconSpinSpeed", FallbackSpinDegrees);
                    _stepSeconds = ReadTuning(ls, "m_IconUpdateSpeed", FallbackStepSeconds);
                    _glowStep = ReadTuning(ls, "m_IconGlowSpeed", FallbackGlowStep);
                    // Min alpha may legitimately be 0 — only NaN/negative/degenerate falls back.
                    float minAlpha = ReadTuningRaw(ls, "m_IconOverlayMinAlpha");
                    _minAlpha = (minAlpha >= 0f && minAlpha < 1f) ? minAlpha : FallbackMinAlpha;
                    VRLog.Info("WorldUI", "Loading indicator art: game spinner sprites acquired " +
                                          $"(base '{DescribeTex(_baseArt)}', overlay '{DescribeTex(_overlayArt)}').");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            if (!_artFailedLogged)
            {
                _artFailedLogged = true;
                VRLog.Warn("WorldUI", $"Loading indicator: reading the game's spinner art failed ({ex.GetType().Name}: " +
                                      $"{ex.Message}) — procedural ring fallback active.");
            }
        }
        if (!_artFailedLogged)
        {
            _artFailedLogged = true;
            VRLog.Warn("WorldUI", "Loading indicator: LoadingScreen spinner sprites unavailable — " +
                                  "procedural ring fallback active.");
        }
        BuildFallbackArt();
    }

    private static string DescribeTex(LayerArt art) =>
        art.Tex != null ? $"{art.Tex.name} {art.Uv}" : "procedural";

    /// <summary>One icon layer from its uGUI object: sprite texture + atlas sub-rect + tint +
    /// authored rect size (aspect and size relative to the base icon).</summary>
    private static LayerArt? ReadLayer(GameObject? iconGo, GameObject? baseGo)
    {
        if (iconGo == null || baseGo == null)
            return null;
        Image? image = iconGo.GetComponent<Image>();
        Sprite? sprite = image != null ? image.sprite : null;
        Texture2D? tex = sprite != null ? sprite.texture : null;
        if (image == null || sprite == null || tex == null)
            return null;

        Rect tr = sprite.textureRect; // throws for tight-packed sprites → caller's catch → fallback
        var art = new LayerArt
        {
            Tex = tex,
            Uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height),
            Tint = image.color,
            Width = Mathf.Max(1f, tr.width),
            Height = Mathf.Max(1f, tr.height),
        };
        // Relative size from the authored uGUI rects (overlay may be larger/smaller than base).
        var iconRect = iconGo.GetComponent<RectTransform>();
        var baseRect = baseGo.GetComponent<RectTransform>();
        if (iconRect != null && baseRect != null && baseRect.rect.width > 1f)
            art.SizeRatio = Mathf.Clamp(iconRect.rect.width / baseRect.rect.width, 0.25f, 4f);
        return art;
    }

    private static float ReadTuning(LoadingScreen ls, string field, float fallback)
    {
        float v = ReadTuningRaw(ls, field);
        return (v > 0f && !float.IsNaN(v) && !float.IsInfinity(v)) ? v : fallback;
    }

    private static float ReadTuningRaw(LoadingScreen ls, string field)
    {
        object? v = AccessTools.Field(typeof(LoadingScreen), field)?.GetValue(ls);
        return v is float f ? f : float.NaN;
    }

    /// <summary>Procedural stand-in: base = faint full ring, overlay = angular-gradient arc
    /// (so the stepped rotation stays visible and the glow pulse has something to pulse).</summary>
    private void BuildFallbackArt()
    {
        _fallbackBaseTex = BuildRingTexture(arcGradient: false);
        _fallbackOverlayTex = BuildRingTexture(arcGradient: true);
        var tint = new Color(0.85f, 0.80f, 0.70f, 1f); // parchment-ish, matches the mod's neutral art
        _baseArt = new LayerArt { Tex = _fallbackBaseTex, Tint = new Color(tint.r, tint.g, tint.b, 0.45f) };
        _overlayArt = new LayerArt { Tex = _fallbackOverlayTex, Tint = tint };
    }

    private static Texture2D BuildRingTexture(bool arcGradient)
    {
        const int size = 128;
        const float outer = 60f, inner = 44f, soft = 2.5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = arcGradient ? "GloomhavenVR.LoadingRingArc" : "GloomhavenVR.LoadingRing",
        };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // Soft-edged annulus.
                float a = Mathf.Clamp01((outer - r) / soft) * Mathf.Clamp01((r - inner) / soft);
                if (arcGradient && a > 0f)
                {
                    // Alpha ramps around the circle → a comet-like arc whose stepped
                    // rotation is unmistakable even at a glance.
                    float t = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI)) + 0.5f; // 0..1 around
                    a *= t * t;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }

    // ---- AnimateIcon replica ---------------------------------------------------------------

    /// <summary>
    /// Replicates <c>LoadingScreen.AnimateIcon()</c> on unscaled time: one discrete step —
    /// rotate BOTH quads by <c>m_IconSpinSpeed</c> about local Z and pulse the overlay alpha
    /// by <c>m_IconGlowSpeed</c> — every <c>m_IconUpdateSpeed</c> seconds (accumulated; the
    /// game's Chronos wait produces the same discrete-step look). A spike frame consumes at
    /// most <see cref="MaxStepsPerFrame"/> steps and drops the rest of the backlog.
    /// </summary>
    private void Animate()
    {
        if (_baseQuad == null || _overlayQuad == null)
            return;
        _accum += Time.unscaledDeltaTime;
        int steps = 0;
        while (_accum >= _stepSeconds && steps++ < MaxStepsPerFrame)
        {
            _accum -= _stepSeconds;
            _baseQuad.Rotate(0f, 0f, _spinDegrees, Space.Self);
            _overlayQuad.Rotate(0f, 0f, _spinDegrees, Space.Self);
            if (_overlayAlpha >= 1f)
                _increaseAlpha = false;
            else if (_overlayAlpha <= _minAlpha)
                _increaseAlpha = true;
            _overlayAlpha = _increaseAlpha
                ? Mathf.Min(1f, _overlayAlpha + _glowStep)
                : Mathf.Max(_minAlpha, _overlayAlpha - _glowStep);
        }
        if (_accum >= _stepSeconds)
            _accum %= _stepSeconds; // spike frame: drop the backlog, keep the phase
        ApplyOverlayAlpha();
    }

    private void ApplyOverlayAlpha()
    {
        if (_overlayMaterial != null && _overlayArt != null)
        {
            Color tint = _overlayArt.Tint;
            _overlayMaterial.color = new Color(tint.r, tint.g, tint.b, tint.a * _overlayAlpha);
        }
    }
}

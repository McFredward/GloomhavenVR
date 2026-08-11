using System;
using BepInEx.Configuration;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// The player's sky choice. Stored as the mod's own enum (BepInEx serialises the member name
/// into the cfg, and the catalog's Choice classification gives it a dropdown for free); the
/// member order IS the dropdown index map (Default=0/Night=1/Sunset=2/Cellar=3), exactly like
/// <c>Cards.BoardMoveMode</c>.
/// </summary>
internal enum SkyStyle
{
    /// <summary>The game's own scenario sky (GH_SkySphere), exactly today's behaviour.</summary>
    Default = 0,

    /// <summary>Clear starry night with the Milky Way (Poly Haven "dikhololo_night", CC0).</summary>
    Night = 1,

    /// <summary>God-ray sunset over rolling hills (Poly Haven "kloppenheim_06", CC0).</summary>
    Sunset = 2,

    /// <summary>Medieval castle cellar interior (Poly Haven "drachenfels_cellar", CC0).</summary>
    Cellar = 3,
}

/// <summary>
/// SKY ALTERNATIVES — swap the game's scenario sky for a bundled 360° panorama.
///
/// THE REQUEST (user, 2026-08-11, verbatim): "Ich möchte nun eine weitere Größere Sache von dir:
/// Alternativen für die Skybox die im Spiel bereits per default integriert ist (von uns
/// modifiziert damit man alles dahinter sehen kann). Gebe mir 3 weitere für geeignete
/// Alternativen. ... Die Skybox soll man entsprechend auch einstellen können, wenn mixed reality
/// aus ist (mixed reality an heißt, dass das die skybox immer deaktiviert ist)."
///
/// WHAT IT DOES
/// ------------
/// A dial (<c>[Sky] Style</c>: Default / Night / Sunset / Cellar, curated in Grafik ▸
/// Darstellung) picks the sky. Default is the game's own sky, bit-identical to today —
/// <see cref="SkyBackdrop"/> keeps making it a non-occluding backdrop and this class does
/// nothing. A non-Default choice:
///
///  1. HIDES the game's sky sphere (<c>GH_SkySphere</c>) via <c>renderer.enabled = false</c>.
///     That is safe for PURE hiding — <see cref="SkyBackdrop"/>'s class doc documents the trap
///     precisely: suppressing a renderer only bites when a CommandBuffer is supposed to REDRAW
///     it, which nothing does here. The sphere is found through <see cref="SkyBackdrop.FindSky"/>
///     (one shared set of name/shader hints), recorded, and re-enabled on restore.
///  2. SHOWS a mod-owned backdrop: a big inverted-look sphere (the built-in sphere primitive
///     mesh — the bundled <c>GloomhavenVR/SkyPanoramic</c> shader culls FRONT faces and samples
///     the equirect panorama from the VIEW DIRECTION, so the mesh's UVs/scale are irrelevant and
///     any enclosing mesh works). Queue Background, ZWrite Off: a pure non-occluding backdrop BY
///     CONSTRUCTION — the same contract SkyBackdrop enforces on the game's sphere, needing none
///     of its machinery. It lives on the MOD LAYER, so only the rig head camera draws it (game
///     cameras and the FlatScreen composites never see it), and it is CENTERED ON THE HEAD every
///     frame with a FIXED world rotation — the standard skybox illusion (no parallax, world-
///     stable stars) at the cost of one transform write per frame while active.
///
/// SCOPE: the backdrop shows whenever VR runs, MR is off and a non-Default sky is chosen — in
/// scenarios (replacing the hidden sphere) AND in scenes that have no sphere at all (Menu2D's
/// black void gets the panorama too; the flat 2D screen is opaque geometry and draws over it).
/// Deliberate: the dial reads "sky", not "scenario sky", and the void is the one place a
/// panorama is pure gain. Only the sphere is swapped — <c>RenderSettings.skybox</c> / ambient
/// light are untouched, so the diorama keeps the game's own lighting.
///
/// MR PRECEDENCE (the user's rule: MR ON ⇒ the sky is ALWAYS off): <see cref="MixedReality.Tick"/>
/// calls <see cref="StandDown"/> FIRST on its MR-on path — the backdrop hides and the game
/// sphere is re-enabled so MR's own <c>HideSkyGeometry</c> sweep records and disables a clean
/// renderer for the chroma key, whatever the dial says. When MR turns off the dial's choice
/// re-applies on the next tick. On the MR-off path this ticks BEFORE SkyBackdrop, and while a
/// non-Default sky is shown SkyBackdrop stands down through the same parameter MR uses (a hidden
/// sphere needs no non-occluding treatment).
///
/// ASSETS (Poly Haven, CC0 — no attribution required, named anyway): loaded LAZILY from the
/// asset bundle on the first non-Default selection via the established probe pattern
/// (<see cref="Cards.PlayTray.OverlayShader"/>: <c>Shader.Find</c>, then
/// <c>AssetBundle.GetAllLoadedAssetBundles()</c> + <c>LoadAsset</c>), so a player who never
/// touches the dial pays zero VRAM. Once loaded, a texture is KEPT for the session — the three
/// 4096x2048 panoramas are a few MB of VRAM each, and keeping them makes live dial-flipping
/// hitch-free; the per-session cost is bounded by the player having tried a sky at all.
///
/// MULTIPLAYER: local presentation only — nothing about the sky is on the wire. [Sky] is not a
/// board section, so the wire-coverage checker does not demand an exemption.
///
/// Per-frame cost discipline: Default/MR-on is an enum read (plus a cheap idempotent early-out);
/// active is one transform write plus a throttled 60-frame sphere re-scan.
/// </summary>
internal static class SkyAlternative
{
    /// <summary>The sky choice. Bound by <see cref="BindConfig"/> into the RIG module file
    /// (<c>dev.gloomhavenvr.rig.cfg</c>, section [Sky]) so the config catalog's force-bind of
    /// <see cref="Rig.RenderQuality"/> surfaces it, and module "rig" files it under the Visual
    /// topic — beside the other look-of-the-picture dials.</summary>
    internal static ConfigEntry<SkyStyle> Style = null!;

    private static bool _bound;

    /// <summary>Frames between sphere re-scans while a non-Default sky is active (the backdrop
    /// can generate late, and a scene change fake-nulls the acquired renderer). Same cadence as
    /// <see cref="SkyBackdrop"/> / MR's sky sweep.</summary>
    private const int ScanIntervalFrames = 60;

    /// <summary>Backdrop sphere RADIUS as a fraction of the head camera's live far clip plane —
    /// far enough that nothing real ever stands behind it, near enough that the far plane never
    /// clips it (the far plane moves under WorldGrab zoom, so this is re-derived per frame).</summary>
    private const float RadiusFarFraction = 0.9f;

    /// <summary>Bundle path of the panorama shader (view-direction equirect, Cull Front,
    /// ZWrite Off, Queue Background — see unity/GloomhavenVR.Assets/Assets/Bundle/Sky/).</summary>
    private const string ShaderBundlePath = "Assets/Bundle/Sky/SkyPanoramic.shader";

    /// <summary>Bundle paths of the three panoramas, indexed by <see cref="SkyStyle"/> (0 = Default = none).</summary>
    private static readonly string?[] TextureBundlePaths =
    {
        null,
        "Assets/Bundle/Sky/Sky_Night.jpg",
        "Assets/Bundle/Sky/Sky_Sunset.jpg",
        "Assets/Bundle/Sky/Sky_Cellar.jpg",
    };

    // Lazily loaded bundle assets — kept for the session once loaded (doc above).
    private static Shader? _shader;
    private static readonly Texture2D?[] Textures = new Texture2D?[4];
    private static bool _missingWarned; // one-shot: bundle lacks the sky assets (older bundle)

    // The game sphere we hid (renderer.enabled = false) — re-enabled on restore. Unity fake-null
    // when its scene unloads; then simply forgotten (the scene took the state with it).
    private static Renderer? _hiddenSphere;
    private static int _scanNextFrame;

    // The mod-owned backdrop.
    private static GameObject? _backdropGo;
    private static Material? _backdropMat;   // ours — destroyed on deactivate
    private static Mesh? _sphereMesh;        // shared built-in primitive mesh — NEVER destroyed
    private static SkyStyle _appliedStyle = SkyStyle.Default;
    private static float _appliedScale = -1f;

    private static bool _active;             // non-Default sky currently shown
    private static bool _loggedActive;       // change-dedup for the on/off log

    /// <summary>
    /// Bind the dial into the rig module's config file. Called from
    /// <see cref="Rig.RenderQuality.Bind"/> (which owns that file), AFTER its own binds — the
    /// same ride-along pattern as FlatScreenStereo on the worldui file.
    /// </summary>
    internal static void BindConfig(ConfigFile file)
    {
        if (_bound)
            return;
        _bound = true;
        Style = file.Bind("Sky", "Style", Defaults.SkyStyle,
            "Which sky surrounds the table (user request 2026-08-11: three bundled alternatives " +
            "for the game's default scenario sky). Default = the game's own animated sky, exactly " +
            "as before. Night = clear starry night with the Milky Way; Sunset = god-ray sunset " +
            "over rolling hills; Cellar = medieval castle cellar vault (all three are CC0 " +
            "panoramas from Poly Haven, bundled with the mod). A non-Default choice hides the " +
            "game's sky sphere and shows the panorama as a pure non-occluding backdrop instead — " +
            "it can never clip menus, the board or the laser, and it also fills the black void of " +
            "scenes that have no sky of their own (the 2D menu). Applies live from the VR menu. " +
            "MIXED REALITY ALWAYS WINS: while MR is on, every sky — the game's and these — is " +
            "off so the chroma key can show your room; the choice re-applies when MR turns off. " +
            "Local presentation only, never synced to peers.");
    }

    // ---- per-frame driver ---------------------------------------------------------------------

    /// <summary>
    /// Per-frame driver for the MR-OFF path, called from <see cref="MixedReality.Tick"/> BEFORE
    /// <see cref="SkyBackdrop.Tick"/>. Returns true while a non-Default sky is being shown — the
    /// caller passes that straight into SkyBackdrop's stand-down parameter (a hidden sphere
    /// needs no non-occluding treatment). Self-gates on <see cref="VRSession.IsRunning"/>.
    /// </summary>
    internal static bool Tick()
    {
        if (!VRSession.IsRunning)
        {
            RestoreAll();
            return false;
        }

        // Ensure the dial is bound (RenderQuality.Bind rides SkyAlternative.BindConfig along).
        if (!_bound)
            Rig.RenderQuality.Bind();

        SkyStyle style = Style.Value;
        if (style == SkyStyle.Default)
        {
            Deactivate();
            return false;
        }

        if (!EnsureAssets(style))
        {
            // Older bundle without the sky assets — leave the game's own sky fully in place
            // (SkyBackdrop keeps treating it) rather than hiding it with nothing to show.
            Deactivate();
            return false;
        }

        HideGameSphere();
        EnsureBackdrop();
        ApplyStyle(style);
        UpdateBackdropPose();

        if (!_active || !_loggedActive)
        {
            _active = true;
            _loggedActive = true;
            VRLog.Info("Core", $"Sky alternative ON — style {style}: the game's sky sphere is hidden " +
                               "(pure renderer.enabled hiding; SkyBackdrop stands down) and the bundled " +
                               "panorama backdrop (mod layer, Background queue, ZWrite Off, head-centered " +
                               "each frame with fixed world rotation) shows instead. MR overrides it off.");
        }
        return true;
    }

    /// <summary>
    /// MR-precedence stand-down, called on <see cref="MixedReality.Tick"/>'s MR-ON path BEFORE
    /// MR's own sky sweep runs: hides the backdrop and RE-ENABLES the game sphere, so
    /// <c>HideSkyGeometry</c> records and disables a clean renderer for the chroma key. Cheap and
    /// idempotent (an early-out when nothing is applied); loaded textures stay cached.
    /// </summary>
    internal static void StandDown() => Deactivate();

    /// <summary>Full teardown (VR stop / hot reload): stand down and drop the one-shot warn latch.
    /// The cached bundle assets are kept — they are session-lifetime by design (class doc).</summary>
    internal static void RestoreAll()
    {
        Deactivate();
        _missingWarned = false;
        _scanNextFrame = 0;
    }

    // ---- the game sphere ----------------------------------------------------------------------

    private static void HideGameSphere()
    {
        // Fake-null: the sphere died with its scene — forget it and re-scan (throttled).
        if (_hiddenSphere == null)
        {
            _hiddenSphere = null;
            if (Time.frameCount < _scanNextFrame)
                return;
            _scanNextFrame = Time.frameCount + ScanIntervalFrames;

            Renderer? sphere = SkyBackdrop.FindSky();
            if (sphere == null)
                return; // no sphere in this scene (Menu2D) — the backdrop still shows
            _hiddenSphere = sphere;
            VRLog.Info("Core", $"Sky alternative: hiding the game's sky sphere '{sphere.gameObject.name}' " +
                               "(renderer.enabled = false — pure hiding, no CommandBuffer redraw involved). " +
                               "Re-enabled on Default / MR-on / VR stop.");
        }
        if (_hiddenSphere.enabled)
            _hiddenSphere.enabled = false;
    }

    private static void RestoreGameSphere()
    {
        if (_hiddenSphere != null && !_hiddenSphere.enabled)
            _hiddenSphere.enabled = true;
        _hiddenSphere = null;
        _scanNextFrame = 0; // a re-activation scans immediately
    }

    // ---- bundle assets ------------------------------------------------------------------------

    /// <summary>Load the panorama shader + the CHOSEN style's texture from whichever loaded
    /// bundle holds them (established probe pattern — see class doc). True when both are ready.</summary>
    private static bool EnsureAssets(SkyStyle style)
    {
        int i = (int)style;
        if (i <= 0 || i >= TextureBundlePaths.Length)
            return false;

        if (_shader == null)
        {
            // Like GloomhavenVR/Overlay, this shader is referenced only by runtime C#, so
            // Shader.Find misses until a bundle load pulls it into memory.
            _shader = Shader.Find("GloomhavenVR/SkyPanoramic");
            if (_shader == null)
            {
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b == null) continue;
                    var s = b.LoadAsset<Shader>(ShaderBundlePath);
                    if (s != null) { _shader = s; break; }
                }
            }
        }

        if (Textures[i] == null)
        {
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b == null) continue;
                var tex = b.LoadAsset<Texture2D>(TextureBundlePaths[i]!);
                if (tex != null) { Textures[i] = tex; break; }
            }
        }

        bool ready = _shader != null && Textures[i] != null;
        if (!ready && !_missingWarned)
        {
            _missingWarned = true;
            VRLog.Warn("Core", $"Sky alternative: bundle assets missing (shader {(_shader == null ? "NOT " : "")}found, " +
                               $"texture '{TextureBundlePaths[i]}' {(Textures[i] == null ? "NOT " : "")}found) — " +
                               "gloomhavenvr.bundle predates the sky feature? The game's own sky stays; " +
                               "update the bundle to use the sky styles.");
        }
        return ready;
    }

    // ---- the backdrop -------------------------------------------------------------------------

    private static void EnsureBackdrop()
    {
        if (_sphereMesh == null)
        {
            // Grab the built-in sphere primitive mesh; DestroyImmediate the temporary GameObject
            // in the same frame so it never draws (the SkyBackdrop quad-mesh pattern). The mesh
            // itself is a shared built-in asset and survives the GameObject.
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            MeshFilter? mf = tmp.GetComponent<MeshFilter>();
            _sphereMesh = mf != null ? mf.sharedMesh : null;
            UnityEngine.Object.DestroyImmediate(tmp);
            if (_sphereMesh == null)
                return;
        }

        if (_backdropMat == null)
            _backdropMat = new Material(_shader!) { name = "GloomhavenVR.SkyAlternative.Backdrop" };

        if (_backdropGo != null)
            return;

        // Built by hand (not CreatePrimitive) so there is NO collider — the backdrop must never
        // catch a laser/poke ray. Mod layer: the head camera composes it in, no game camera does.
        _backdropGo = new GameObject("GloomhavenVR.SkyAlternative.Backdrop");
        _backdropGo.layer = VRLayers.ModLayer;
        MeshFilter filter = _backdropGo.AddComponent<MeshFilter>();
        filter.sharedMesh = _sphereMesh;
        MeshRenderer renderer = _backdropGo.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _backdropMat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _backdropGo.transform.rotation = Quaternion.identity; // fixed world rotation — world-stable stars
        _appliedScale = -1f;
    }

    private static void ApplyStyle(SkyStyle style)
    {
        if (_appliedStyle == style || _backdropMat == null)
            return;
        _appliedStyle = style;
        Texture2D? tex = Textures[(int)style];
        if (tex != null)
            _backdropMat.mainTexture = tex; // _MainTex; _Tint/_RotationDeg keep their shader defaults
        if (_loggedActive)
            VRLog.Info("Core", $"Sky alternative: style switched to {style}.");
    }

    /// <summary>
    /// One transform write per frame while active: center the sphere on the head (the standard
    /// skybox illusion — the sky never comes closer, whatever the player does) and size it just
    /// under the LIVE far clip plane (TickClipPlanes moves it under WorldGrab zoom). Rotation is
    /// never touched after creation. Scale is only written when the far plane actually changed.
    /// </summary>
    private static void UpdateBackdropPose()
    {
        if (_backdropGo == null)
            return;
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
            return;

        Transform t = _backdropGo.transform;
        t.position = head.transform.position;

        // Built-in sphere primitive: diameter 1 at scale 1 → localScale = 2 * radius.
        float scale = 2f * RadiusFarFraction * Mathf.Max(1f, head.farClipPlane);
        if (!Mathf.Approximately(scale, _appliedScale))
        {
            _appliedScale = scale;
            t.localScale = new Vector3(scale, scale, scale);
        }
    }

    // ---- deactivate ---------------------------------------------------------------------------

    /// <summary>Back to vanilla: re-enable the game sphere, destroy the backdrop object and OUR
    /// material. The shared primitive mesh reference and the loaded textures/shader stay (the
    /// mesh is a built-in asset; the textures are session-cached by design).</summary>
    private static void Deactivate()
    {
        if (!_active && _backdropGo == null && _hiddenSphere == null)
            return;

        RestoreGameSphere();
        if (_backdropGo != null)
        {
            UnityEngine.Object.Destroy(_backdropGo);
            _backdropGo = null;
        }
        if (_backdropMat != null)
        {
            UnityEngine.Object.Destroy(_backdropMat);
            _backdropMat = null;
        }
        _appliedStyle = SkyStyle.Default;
        _appliedScale = -1f;
        if (_active)
            VRLog.Info("Core", "Sky alternative OFF — game sphere restored, panorama backdrop destroyed.");
        _active = false;
        _loggedActive = false;
    }
}

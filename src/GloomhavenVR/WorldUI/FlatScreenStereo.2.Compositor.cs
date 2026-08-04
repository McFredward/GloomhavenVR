using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreenStereo
{
    public FlatScreenStereo()
    {
        _preRenderHook = OnPreRenderCamera; // cached delegate — one allocation, ever
        _postRenderHook = OnPostRenderCamera; // worldMap material-override restore
        _preCullHook = OnPreCullCamera; // apply map matrices before the mirror camera's culling
        BindConfig();
    }

    /// <summary>
    /// Bind-once for the stereo compositor's [WorldUI] entries. INTERNAL rather than private since
    /// 2026-07: these 25 entries live on WorldUIConfig's file but are only bound when a
    /// <see cref="FlatScreenStereo"/> is CONSTRUCTED, so the in-VR config browser force-binds them
    /// (<c>ConfigCatalog.EnsureBound</c>) instead of showing a hole where a quarter of the
    /// world-UI settings should be. Binding is pure — it touches no camera, no scene object and no
    /// instance state — so calling it from the settings pane is safe at any time.
    /// </summary>
    internal static void BindConfig()
    {
        if (s_stereoScreen != null)
            return;
        ConfigFile file = WorldUIConfig.Master.ConfigFile;
        s_stereoScreen = file.Bind("WorldUI", "StereoScreen", Defaults.StereoScreen,
            "Render the floating 2D screen WITH stereo depth (3D-movie/window effect): the " +
            "captured 3D menu content (campaign map, town, slideshow scene) is rendered once " +
            "per eye via mod-owned mirror cameras while flat UI stays exactly on the screen " +
            "plane. Interaction (laser, poke, virtual mouse) and the desktop mirror are " +
            "unaffected. Costs one extra render of the menu scene per frame while the screen " +
            "is visible. Off = single mono RenderTexture, exactly the pre-stereo behavior.");
        s_depthStrength = file.Bind("WorldUI", "ScreenDepthStrength", Defaults.ScreenDepthStrength,
            "Strength of the flat screen's stereo depth (scales the per-eye separation " +
            "linearly). 1 = geometrically derived from your HMD IPD (window-accurate, " +
            "slightly understated by design); smaller = flatter/more comfortable; " +
            "0 = mono (same as StereoScreen=false).");
        s_videoDepthLayer = file.Bind("WorldUI", "VideoDepthLayer", Defaults.VideoDepthLayer,
            "Keep stereo depth while a fullscreen 2D video plays on the screen (main-menu " +
            "ambient movie, story videos): the VideoPlayer stays untouched in its vanilla " +
            "camera-plane mode and BOTH eyes show slightly shifted copies of the captured " +
            "background, so the whole background (essentially just the video then) reads " +
            "VideoDepth meters BEHIND the glass UI — background recedes, menu floats in " +
            "front, no artificial geometry. Off = stereo is fully suspended (mono) while " +
            "any camera-plane video plays.");
        s_videoDepth = file.Bind("WorldUI", "VideoDepth", Defaults.VideoDepth,
            "How far BEHIND the screen plane the background reads while a fullscreen 2D " +
            "video plays, in real meters (VideoDepthLayer). Disparity p = IPD*V/(D+V) with " +
            "D = ScreenDistance: at the 1.6 m default screen and 2.2 m depth the video reads " +
            "at 3.8 m (~2.4x the screen distance, ~36 mm disparity — below the ~63 mm " +
            "divergence limit; clamped to 55 mm regardless). Raised from 0.8 after test #19 " +
            "(the recession read too subtle). 0 = video on the screen plane (no video depth).");
        s_parallaxScale = file.Bind("WorldUI", "ScreenParallaxScale", Defaults.ScreenParallaxScale,
            "Amplifies the stereo screen's scene-INTERNAL depth (test #16: far menu scenery " +
            "read flat at geometric settings). Separation AND convergence are multiplied by " +
            "the same factor, so the at-infinity disparity (their ratio) stays constant and " +
            "comfortable while depth differences inside the captured scene grow this many " +
            "times stronger — diorama-behind-glass instead of flat photo. 1 = strict window " +
            "geometry; clamped to 1-60.");
        // ---- DEPRECATED map keys -------------------------------------------------------------
        // Every map key from here to the end of this method has NO effect — EXCEPT MapAlbedoRender,
        // which is live and stays where it is (bind order is not reshuffled to group them). Each of
        // the other 19 configured a map-capture design that was disproven on hardware and whose code
        // has been removed. They stay BOUND on purpose —
        // charter §5: an unread config key is still a user's persisted setting, and unbinding it
        // drops the line from their .cfg on the next write. Each description now says plainly that
        // it does nothing and what superseded it, so a knob that lies becomes a knob that admits it.
        // (Precedent for the wording: [Cards] HeldTiltDegrees, "LEGACY — no longer used ... kept
        // only so existing config files load cleanly".)
        s_leftMirrorFallback = file.Bind("WorldUI", "ScreenLeftMirrorFallback", Defaults.ScreenLeftMirrorFallback,
            "DEPRECATED — no effect, and it never had one: this key has no reader anywhere in the " +
            "mod. It presented itself as the master switch for the black-map rescue; the actual " +
            "switch is MapAlbedoRender. Kept bound so existing .cfg files load unchanged.");
        s_mapAlbedoRender = file.Bind("WorldUI", "MapAlbedoRender", Defaults.MapAlbedoRender,
            "THE MAP FIX (default ON): render the campaign map's parchment UNLIT via a mod-owned " +
            "FORWARD camera into a PRIVATE RenderTexture, which the screen quad then shows. The map " +
            "is ordinary mesh geometry (MapChoreographer.worldMap / cityMap, GH_WorldMap materials " +
            "whose albedo lives in _Alb / _MainTex), but its deferred Amplify shader is never lit " +
            "into any RenderTexture we own and its backbuffer is unreadable under XR — so instead of " +
            "capturing the game's render, the mod re-renders the mesh with a GloomhavenVR/MapUnlit " +
            "material per submesh (albedo -> _MainTex, UV sampled on the GPU from the mesh's own " +
            "TexCoord0), swapped on only for our render and restored the same frame (rendering-only, " +
            "multiplayer-safe). A top-down painted map reads correct unlit. Off = detect the black " +
            "map but leave the base RT as-is (black).");
        s_mapAlbedoOriginalMat = file.Bind("WorldUI", "MapAlbedoOriginalMaterial", Defaults.MapAlbedoOriginalMaterial,
            "DEPRECATED — no effect. It chose between rendering the map with the game's own Amplify " +
            "material and an override material; the map is now always drawn with GloomhavenVR/" +
            "MapUnlit, and both alternatives it named are gone. Kept bound so existing .cfg files " +
            "load unchanged.");
        s_mapAlbedoAmbient = file.Bind("WorldUI", "MapAlbedoAmbient", Defaults.MapAlbedoAmbient,
            "DEPRECATED — no effect. It forced a bright ambient during the map's forward render, " +
            "back when that render was LIT. MapUnlit is unlit, so no ambient value can change the " +
            "map; the code that read this key was removed. (When it did run it was measured: an " +
            "ambient of 4 only turned a flat dark parchment into a flat brighter one.) Kept bound so " +
            "existing .cfg files load unchanged.");
        s_mapAlbedoLight = file.Bind("WorldUI", "MapAlbedoLight", Defaults.MapAlbedoLight,
            "DEPRECATED — no effect. It added a mod directional light during the map's forward " +
            "render, in case the map detail was normal-mapped relief. MapUnlit is unlit, so a light " +
            "cannot affect it; the code that read this key was removed. Kept bound so existing .cfg " +
            "files load unchanged.");

        s_mapCaptureMode = file.Bind("WorldUI", "MapCaptureMode", Defaults.MapCaptureMode,
            "DEPRECATED — no effect. The campaign map is always rendered by the mod's forward albedo " +
            "camera into a private RenderTexture. The 'passive-deferred' strategy (1) this selected " +
            "was disproven — the game's deferred map camera renders black into any RenderTexture we " +
            "own, which no image-effect stripping can change — and the 'texture blit' strategy (2) " +
            "was never implemented at all. Both code paths were removed. Kept bound so existing .cfg " +
            "files load unchanged.");
        s_mapStripBeautify = file.Bind("WorldUI", "MapStripBeautify", Defaults.MapStripBeautify,
            "DEPRECATED — no effect (belonged to MapCaptureMode 1, removed). Kept bound so existing " +
            ".cfg files load unchanged.");
        s_mapStripVolumetricFog = file.Bind("WorldUI", "MapStripVolumetricFog", Defaults.MapStripVolumetricFog,
            "DEPRECATED — no effect (belonged to MapCaptureMode 1, removed). Kept bound so existing " +
            ".cfg files load unchanged.");
        s_mapStripSSAO = file.Bind("WorldUI", "MapStripSSAO", Defaults.MapStripSSAO,
            "DEPRECATED — no effect (belonged to MapCaptureMode 1, removed). Kept bound so existing " +
            ".cfg files load unchanged.");
        s_mapStripPostProcess = file.Bind("WorldUI", "MapStripPostProcess", Defaults.MapStripPostProcess,
            "DEPRECATED — no effect (belonged to MapCaptureMode 1, removed). Kept bound so existing " +
            ".cfg files load unchanged.");
        s_mapTexFlipX = file.Bind("WorldUI", "MapTexFlipX", Defaults.MapTexFlipX,
            "DEPRECATED — no effect (belonged to MapCaptureMode 2, which was never implemented). " +
            "Kept bound so existing .cfg files load unchanged.");
        s_mapTexFlipY = file.Bind("WorldUI", "MapTexFlipY", Defaults.MapTexFlipY,
            "DEPRECATED — no effect (belonged to MapCaptureMode 2, which was never implemented). " +
            "Kept bound so existing .cfg files load unchanged.");
        s_mapTexSwapDiag = file.Bind("WorldUI", "MapTexSwapDiag", Defaults.MapTexSwapDiag,
            "DEPRECATED — no effect (belonged to MapCaptureMode 2, which was never implemented). " +
            "Kept bound so existing .cfg files load unchanged.");
        s_mapStripAllImageEffects = file.Bind("WorldUI", "MapStripAllImageEffects", Defaults.MapStripAllImageEffects,
            "DEPRECATED — no effect (belonged to MapCaptureMode 1, removed). Kept bound so existing " +
            ".cfg files load unchanged.");

        // Map UV correction knobs — DEPRECATED, no reader. They configured the CPU uv0-rebuild path
        // (a mod-owned corrected mesh copy + Sprites/Default), which was removed once the mesh was
        // proven to carry a correct TexCoord0 that GloomhavenVR/MapUnlit samples on the GPU
        // (53af144). Kept BOUND so existing .cfg files keep loading unchanged; see charter §5.
        s_mapUvSource = file.Bind("WorldUI", "MapUvSource", Defaults.MapUvSource,
            "DEPRECATED — no effect. The map's UV is sampled on the GPU from the mesh's own " +
            "TexCoord0 by GloomhavenVR/MapUnlit; the CPU uv0-rebuild path this configured was " +
            "removed. (The mesh was extracted offline and rasterized with its own UV0: it produces " +
            "the complete correct map, so there is nothing to correct.) Kept bound so existing .cfg " +
            "files load unchanged.");
        s_mapUvSwapUV = file.Bind("WorldUI", "MapUvSwapUV", Defaults.MapUvSwapUV,
            "DEPRECATED — no effect (belonged to the removed CPU uv0-rebuild path). Kept bound so " +
            "existing .cfg files load unchanged.");
        s_mapUvFlipU = file.Bind("WorldUI", "MapUvFlipU", Defaults.MapUvFlipU,
            "DEPRECATED — no effect (belonged to the removed CPU uv0-rebuild path). Kept bound so " +
            "existing .cfg files load unchanged.");
        s_mapUvFlipV = file.Bind("WorldUI", "MapUvFlipV", Defaults.MapUvFlipV,
            "DEPRECATED — no effect (belonged to the removed CPU uv0-rebuild path). Kept bound so " +
            "existing .cfg files load unchanged.");
        s_mapUvChannel = file.Bind("WorldUI", "MapUvChannel", Defaults.MapUvChannel,
            "DEPRECATED — no effect, and deliberately not read: the shader's UV channel is " +
            "hard-coded to 0 so a stale persisted value cannot break the map. Kept bound so existing " +
            ".cfg files load unchanged.");
        s_mapUvComponent = file.Bind("WorldUI", "MapUvComponent", Defaults.MapUvComponent,
            "DEPRECATED — no effect (belonged to the removed CPU uv0-rebuild path). Kept bound so " +
            "existing .cfg files load unchanged.");

    }

    /// <summary>[WorldUI] MapAlbedoRender — render the map parchment unlit via a mod forward camera (class doc MAP ALBEDO RENDER).</summary>
    internal static bool MapAlbedoRenderOn => s_mapAlbedoRender?.Value ?? true;
    private static float DepthStrength => Mathf.Clamp(s_depthStrength?.Value ?? 1f, 0f, 3f);

    private static float ParallaxScale => Mathf.Clamp(s_parallaxScale?.Value ?? 6f, 1f, 60f);

    /// <summary>
    /// Colour RenderTexture factory for the flat-screen map/eye path — plain
    /// <c>RenderTextureReadWrite.Default</c>. (The sRGB/colorspace fix was proven a
    /// no-op: the rig renders in Gamma colorspace, so sRGB read/write yields sRGB=False
    /// regardless.)
    /// </summary>
    internal static RenderTexture CreateColorRt(int width, int height, int depth, string name)
    {
        var rt = new RenderTexture(width, height, depth, RenderTextureFormat.Default, RenderTextureReadWrite.Default)
        {
            name = name,
            antiAliasing = 1,
        };
        // DEFERRED FIX: the game's map camera renders DeferredShading, and deferred lighting resolves
        // through a STENCIL buffer (the light pass masks lit pixels via stencil). A plain depth RT (the
        // rig reported depth=32 = 32-bit float depth, NO stencil) makes the deferred light pass fail →
        // the map renders flat/dark into our RT while forward content (character portraits, combat)
        // captures fine. Force a 24-bit depth + 8-bit stencil buffer whenever we ask for a depth buffer.
        if (depth >= 24)
            rt.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt;
        return rt;
    }

    /// <summary>Human-readable facts of an RT (format, graphicsFormat, sRGB flag, depth) for the setup logs.</summary>
    internal static string DescribeRt(RenderTexture? rt)
    {
        if (rt == null)
            return "null";
        bool srgb = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(rt.graphicsFormat);
        return $"'{rt.name}' {rt.width}x{rt.height} fmt {rt.format}/{rt.graphicsFormat} sRGB={srgb} depth={rt.depth} aa={rt.antiAliasing}";
    }

    private static bool WantActive(RenderTexture? leftRt) =>
        leftRt != null
        && VRSession.IsRunning
        && Rig.VRRigDriver.HeadCamera != null
        && (s_stereoScreen?.Value ?? false)
        && DepthStrength > 0f;

    // ---- lifecycle -------------------------------------------------------------------------

    /// <summary>
    /// Per-tick lifecycle (called from FlatScreen.CaptureStack while the screen is
    /// visible): engage/disengage per config + session state, keep the right RT in
    /// step with the left one, refresh IPD and the derived scene-unit geometry.
    /// <paramref name="introActive"/> = the screen currently shows a pre-menu scene
    /// (class doc INTRO GUARD — forces the suspension).
    /// </summary>
    internal void Tick(RenderTexture? leftRt, Renderer? quadRenderer, bool introActive)
    {
        _leftRt = leftRt;
        _quadMaterial = quadRenderer != null ? quadRenderer.sharedMaterial : null;
        _introGuard = introActive;

        if (!WantActive(leftRt))
        {
            if (_active)
                Deactivate("disabled (config/session/rig state)");
            return;
        }

        // Right RT mirrors the left one's dimensions (they must match — the pointer
        // pixel mapping and the quad UVs are shared).
        if (_rtRight != null && (_rtRight.width != leftRt!.width || _rtRight.height != leftRt.height))
        {
            ReleaseRightRt();
        }
        if (_rtRight == null)
        {
            _rtRight = CreateColorRt(leftRt!.width, leftRt.height, 24, "GloomhavenVR.FlatScreenRT.Right");
            _rtRight.Create();
        }

        if (!_active)
        {
            _active = true;
            if (!_hooked)
            {
                Camera.onPreCull += _preCullHook; // map matrices BEFORE culling (else quads culled out)
                Camera.onPreRender += _preRenderHook;
                Camera.onPostRender += _postRenderHook; // worldMap override restore
                _hooked = true;
            }
            _eyeObsCount = 0; // re-log the observed eye-pass pattern per activation
            _shiftRtFailed = false; // a failed shifted RT gets a fresh chance per activation
            VRLog.Info("WorldUI", "STEREO SCREEN ACTIVE — flat screen renders per eye " +
                                  "(left RT = game cameras, right RT = mod mirror cameras; " +
                                  "menu-scene rendering doubles while the screen is visible). " +
                                  $"IPD {_ipdMeters * 1000f:F1} mm, strength {DepthStrength:F2}.");
        }

        SampleIpd();

        // Real meters → captured-scene units via the rig's real↔game scale relation
        // (1 in the menu rig; diorama scale in scenarios — see class doc). Both terms
        // carry the SAME parallax factor (class doc PARALLAX SCALE: sep/conv ratio —
        // and with it the at-infinity comfort — is invariant; only scene-internal
        // depth is amplified), and convergence targets the quad's actual distance
        // (ScreenDistance), so content at screen distance lands exactly on the image.
        float scale = PanelLayout.WorldScale;
        float parallax = ParallaxScale;
        _sepScene = _ipdMeters * DepthStrength * scale * parallax;
        _convScene = Mathf.Max(MinConvergenceMeters,
            WorldUIConfig.ScreenDistance.Value) * scale * parallax;
        _videoShiftUv = ComputeVideoShiftUv();
    }

    /// <summary>
    /// Per-eye UV shift for the video depth shift (class doc VIDEO DEPTH SHIFT —
    /// disparity math). Pure screen-plane geometry in REAL meters, so no WorldScale
    /// or parallax factor applies: p = IPD·strength·V/(D+V), clamped below the
    /// divergence limit AND below the overscan margin (a larger shift would sample
    /// past the RT edge — depth silently caps instead of showing void).
    /// </summary>
    private float ComputeVideoShiftUv()
    {
        float depth = Mathf.Clamp(s_videoDepth?.Value ?? 2.2f, 0f, 5f);
        if (depth <= 0f)
            return 0f;
        float distance = Mathf.Max(0.1f, WorldUIConfig.ScreenDistance.Value);
        float disparity = Mathf.Min(
            _ipdMeters * DepthStrength * depth / (distance + depth), MaxVideoDisparityMeters);
        float halfUv = 0.5f * disparity / Mathf.Max(0.5f, WorldUIConfig.ScreenWidth.Value);
        return Mathf.Min(halfUv, (1f - 1f / VideoOverscan) * 0.5f);
    }

    /// <summary>Full teardown: mirrors, mod RTs, render hook; quad texture back to the left RT.</summary>
    internal void Deactivate(string reason)
    {
        ReleaseAlbedo();
        ReleaseMirrors();
        if (_hooked)
        {
            Camera.onPreCull -= _preCullHook;
            Camera.onPreRender -= _preRenderHook;
            Camera.onPostRender -= _postRenderHook;
            _hooked = false;
        }
        if (_quadMaterial != null && _leftRt != null && _quadMaterial.mainTexture != _leftRt)
            _quadMaterial.mainTexture = _leftRt;
        ReleaseRightRt();
        ReleaseShiftRt();
        ReleaseProbeRt();
        ReleaseAlbedoProbeRt();
        ReleaseMapRt();
        _probeGen++;                 // invalidate any in-flight probe callback
        _probePending = false;
        _albedoProbePending = false;
        _mapBaseCapture = false;
        _blackConsecutive = 0;
        _nonBlackMapConsecutive = 0;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        if (_active)
        {
            _active = false;
            _videoSuspended = false;
            _videoShift = false;
            _shiftRtFailed = false;
            VRLog.Info("WorldUI", $"Stereo screen deactivated ({reason}) — mirrors destroyed, " +
                                  "right RT released, quad back on the mono RT.");
        }
    }

    private void ReleaseRightRt()
    {
        if (_rtRight == null)
            return;
        _rtRight.Release();
        Object.Destroy(_rtRight);
        _rtRight = null;
    }

    private void ReleaseShiftRt()
    {
        if (_rtLeftShifted == null)
            return;
        _rtLeftShifted.Release();
        Object.Destroy(_rtLeftShifted);
        _rtLeftShifted = null;
    }

    private void ReleaseProbeRt()
    {
        if (_probeRt == null)
            return;
        _probeRt.Release();
        Object.Destroy(_probeRt);
        _probeRt = null;
    }

    private void ReleaseAlbedoProbeRt()
    {
        if (_albedoProbeRt == null)
            return;
        _albedoProbeRt.Release();
        Object.Destroy(_albedoProbeRt);
        _albedoProbeRt = null;
    }

    // ---- per-tick stack sync ---------------------------------------------------------------

    /// <summary>Mark-and-sweep begin: every entry must be re-claimed by <see cref="SyncCamera"/>.</summary>
    internal void BeginStackSync()
    {
        for (int i = 0; i < _mirrors.Count; i++)
            _mirrors[i].Synced = false;

        // Late-video throttles (class doc VIDEO DISCOVERY). The global sweep runs
        // FIRST so the SyncCamera calls of this very tick already see a discovered
        // player and can shift/suspend immediately.
        _videoRecheckDue = Time.frameCount - _videoRecheckFrame >= VideoRecheckIntervalFrames;
        if (_videoRecheckDue)
            _videoRecheckFrame = Time.frameCount;
        if (_active && _mirrors.Count > 0
            && Time.frameCount - _videoSweepFrame >= VideoSweepIntervalFrames)
        {
            _videoSweepFrame = Time.frameCount;
            SweepForCameraPlaneVideos();
        }
    }

    /// <summary>
    /// Global sweep for camera-plane VideoPlayers living on GameObjects OTHER than
    /// their camera (the intro binds via targetCamera — test #16 one-eyed intro).
    /// Throttled to every <see cref="VideoSweepIntervalFrames"/> frames while stereo
    /// is active and mirrors exist; the FindObjectsOfType allocation is accepted at
    /// that rate — a missed player costs a whole eye, not a frame-time spike.
    /// </summary>
    private void SweepForCameraPlaneVideos()
    {
        VideoPlayer[] players = Object.FindObjectsOfType<VideoPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            VideoPlayer player = players[i];

            MirrorEntry? entry = null;
            Camera? bound = player.targetCamera;
            if (bound != null && _bySource.TryGetValue(bound, out MirrorEntry byTarget))
                entry = byTarget;
            else
            {
                Camera host = player.GetComponent<Camera>();
                if (host != null && _bySource.TryGetValue(host, out MirrorEntry byHost))
                    entry = byHost;
            }

            if (player.renderMode != VideoRenderMode.CameraNearPlane
                && player.renderMode != VideoRenderMode.CameraFarPlane)
            {
                // Gate diagnostic (test #20): a player bound to a MIRRORED camera in
                // a non-camera-plane mode needs no depth shift BY DESIGN (its frames
                // reach both eyes through the normal camera render) — but if a video
                // ever reads flat/one-eyed on hardware, this line rules the mode in
                // or out. Keyed player+mode so a mode CHANGE re-logs.
                if (entry != null
                    && _videoGateLogged.Add(player.GetInstanceID() * 31L + (int)player.renderMode))
                    VRLog.Info("WorldUI", $"Stereo screen sweep: VideoPlayer on " +
                                          $"'{player.gameObject.name}' is bound to captured " +
                                          $"'{entry.Source.name}' but renderMode {player.renderMode} " +
                                          "is not camera-plane — no depth shift needed/possible.");
                continue;
            }
            if (entry == null)
            {
                // Camera-plane player whose camera we did NOT capture: the depth
                // shift cannot see it and no suspension applies — name it once.
                if (_videoGateLogged.Add(player.GetInstanceID()))
                    VRLog.Info("WorldUI", $"Stereo screen sweep: camera-plane VideoPlayer on " +
                                          $"'{player.gameObject.name}' (targetCamera " +
                                          $"'{(bound != null ? bound.name : "<null>")}') matches no " +
                                          "captured camera — outside the depth shift's reach.");
                continue;
            }
            // Only fill EMPTY slots (Unity fake-null included — a destroyed player is
            // replaced, a live cached one is never thrashed by a second candidate).
            if (entry.Video != null)
                continue;
            entry.Video = player;
            LogLateVideo(player, entry.Source, "global sweep, bound via targetCamera from GO '"
                                               + player.gameObject.name + "'");
        }
    }

    private void LogLateVideo(VideoPlayer player, Camera source, string how)
    {
        if (!_videoLogged.Add(player.GetInstanceID()))
            return;
        VRLog.Info("WorldUI", $"Stereo screen: camera-plane VideoPlayer discovered LATE for " +
                              $"'{source.name}' ({how}) — the video depth shift/suspension now " +
                              "applies (without it this video would render in one eye only).");
    }

    /// <summary>
    /// Ensure a mirror exists for this captured camera and copy the source's live
    /// state onto it (field copies only — no allocations after first capture).
    /// </summary>
    internal void SyncCamera(Camera source)
    {
        if (!_active || _rtRight == null)
            return;

        if (!_bySource.TryGetValue(source, out MirrorEntry entry))
            entry = CreateMirror(source);

        entry.Synced = true;
        entry.SourceOn = source.isActiveAndEnabled;
        // Players can be ADDED to the camera GO after mirror creation — re-check
        // empty slots on the throttled gate (destroyed players are fake-null and
        // re-checked too; class doc VIDEO DISCOVERY).
        if (entry.Video == null && _videoRecheckDue)
        {
            entry.Video = source.GetComponent<VideoPlayer>();
            if (entry.Video != null)
                LogLateVideo(entry.Video, source, "component appeared on the camera's GameObject");
        }
        entry.VideoActive = entry.SourceOn && IsNearPlaneVideoActive(entry.Video, source);

        if (!entry.SourceOn)
            return; // mirror gets disabled in EndStackSync; nothing to copy

        Camera mirror = entry.Mirror;
        Transform st = source.transform;

        // Eye offset: right eye = source pose shifted along the source's +right
        // (only 3D cameras arrive here — the UI lives on FlatScreen's glass layer).
        if (_sepScene > 0f)
            entry.MirrorTransform.SetPositionAndRotation(st.position + st.right * _sepScene, st.rotation);
        else
            entry.MirrorTransform.SetPositionAndRotation(st.position, st.rotation);

        // Live field copies: the source's CURRENT clear flags are the EFFECTIVE ones
        // (FlatScreen's base-clear force + overlay demotion already applied), so the
        // right RT composes under the exact same clear policy as the left.
        mirror.clearFlags = source.clearFlags;
        mirror.backgroundColor = source.backgroundColor;
        mirror.cullingMask = source.cullingMask & ~VRLayers.ModLayerMask; // never our quad/hands (feedback!)
        mirror.depth = source.depth;                                     // same per-RT compositing order
        mirror.rect = source.rect;
        mirror.nearClipPlane = source.nearClipPlane;
        mirror.farClipPlane = source.farClipPlane;
        mirror.orthographic = source.orthographic;
        mirror.orthographicSize = source.orthographicSize;
        mirror.fieldOfView = source.fieldOfView;
        mirror.allowHDR = source.allowHDR;
        mirror.allowMSAA = source.allowMSAA;
        mirror.useOcclusionCulling = source.useOcclusionCulling;
        if (mirror.targetTexture != _rtRight)
            mirror.targetTexture = _rtRight;

        // Projection: copy the source matrix plus the off-axis convergence shift
        // (see class doc — lens shift, not toe-in).
        // Derivation: the right camera sits +s along +right; a point straight ahead
        // at the convergence distance D lands at NDC x = -m00*s/D in it, so
        // m02 -= m00*s/D translates the image so that point matches the left eye
        // (zero disparity at D; uncrossed/behind-screen beyond it).
        Matrix4x4 proj = source.projectionMatrix;
        if (_sepScene > 0f && _convScene > 1e-4f)
            proj.m02 -= proj.m00 * (_sepScene / _convScene);
        mirror.projectionMatrix = proj;
    }

    /// <summary>
    /// Mark-and-sweep end: drop mirrors whose source died/left the stack, engage the
    /// video depth shift while any camera-plane video is active (class doc VIDEO
    /// DEPTH SHIFT), decide the fallback suspension for THIS frame, and apply the
    /// enabled state (mirrors render only while neither shift nor suspension runs).
    /// </summary>
    internal void EndStackSync()
    {
        if (!_active)
            return;

        bool anyVideo = false;
        string? videoSource = null;
        Camera? mapSource = null; // lowest-depth live 3D source (= the game MapCamera) for the albedo clone
        for (int i = _mirrors.Count - 1; i >= 0; i--)
        {
            MirrorEntry entry = _mirrors[i];
            if (!entry.Synced || entry.Source == null)
            {
                DestroyMirrorAt(i);
                continue;
            }
            if (entry.VideoActive && !anyVideo)
            {
                anyVideo = true;
                videoSource = entry.Source.name;
            }
            if (entry.SourceOn && (mapSource == null || entry.Source.depth < mapSource.depth))
                mapSource = entry.Source;
        }

        // FAST positive map-open engage (user report: brown pre-map flash — see TickFastMapEngage's
        // doc block): decide map engagement at the map-open event itself instead of waiting ~1.25 s
        // of probe cadence. Runs BEFORE the suspend/shift decision so the SAME tick already routes
        // the map mono, turns the mirrors off and configures the mod forward camera.
        TickFastMapEngage(mapSource);

        // Decision (class doc): the intro guard forces the suspension outright — zero
        // shift, the intro must remain verified-identical. The campaign-map albedo
        // render (class doc MAP ALBEDO RENDER) also drives BOTH eyes MONO from the base
        // RT — the mod albedo camera fills it bright, no per-eye parallax. Otherwise an
        // active camera-plane video engages the uniform depth shift; if the shift path
        // is unavailable (kill switch / zero depth / no shifted RT) the suspension
        // fallback takes over — never one-eyed, never black.
        bool depthLayer = s_videoDepthLayer?.Value ?? true;
        bool shift = false;
        bool suspend = false;
        string? suspendWhy = null;
        if (_introGuard)
        {
            suspend = true;
        }
        else if (_mapBaseCapture)
        {
            // Campaign map: the base RT carries the MONO map (mirror camera + world-space quads). We must
            // NOT suspend — suspension collapses the SCREEN LAYER SPLIT and drops the game's UI glass RT
            // (markers, quest list, shields). Instead keep the split ROUTING (suspend stays false, so
            // FlatScreen keeps UI cameras on the glass RT), and force BOTH eyes to the base RT in
            // OnPreRenderCamera (mono; per-eye parallax is explicitly dropped for the map). Because the
            // mirror camera matches the game map camera, the glass UI markers/laser align with the map.
            if (!_mapMonoLogged)
            {
                _mapMonoLogged = true;
                VRLog.Info("WorldUI", "Campaign map MONO: both eyes show the base RT (mirror-camera map); the " +
                                      "split stays ROUTING so the game UI/markers keep rendering on the glass RT " +
                                      "and composite over the map — the map is NOT suspended.");
            }
        }
        else if (anyVideo)
        {
            if (!depthLayer)
                suspendWhy = "VideoDepthLayer disabled";
            else if (_videoShiftUv <= 0f)
                suspendWhy = "VideoDepth is 0 (content on the screen plane = plain mono)";
            else if (!EnsureShiftRt())
                suspendWhy = "shifted-RT unavailable";
            else
                shift = true;
            suspend = !shift;
        }

        if (shift != _videoShift)
        {
            _videoShift = shift;
            VRLog.Info("WorldUI", shift
                ? $"Depth shift ENGAGED for '{videoSource}': the vanilla camera-plane video keeps " +
                  "rendering into the left RT untouched; mirrors off, both eyes show shifted copies " +
                  $"of it (±{_videoShiftUv:F4} UV, {VideoOverscan:F2}x overscan) — the background " +
                  "reads behind the glass UI."
                : "Depth shift RELEASED — no camera-plane video active; per-eye mirror parallax re-engages.");
        }

        if (suspend != _videoSuspended)
        {
            _videoSuspended = suspend;
            VRLog.Info("WorldUI", suspend
                ? (_introGuard
                    ? "Stereo screen SUSPENDED — intro guard: pre-menu scenes force identical " +
                      "eyes (both eyes = left RT, zero shift; the intro must remain " +
                      "verified-identical)."
                    : _mapBaseCapture
                        ? "Stereo screen SUSPENDED — campaign map albedo render: both eyes show the " +
                          "base RT (the mod forward camera draws the parchment unlit into it)."
                        : "Stereo screen SUSPENDED — the depth shift is unavailable " +
                          $"({suspendWhy}); both eyes show the left RT until it resumes.")
                : "Stereo screen RESUMED — per-eye rendering re-engaged.");
        }

        // Campaign map (class doc MAP ALBEDO RENDER): deferred→our-RT is a hard wall (the game's
        // DeferredShading map camera renders pure black into any off-screen RT we own, stencil or not;
        // only Forward content captures — proven by the Forward character portraits). So the mod's own
        // FORWARD camera re-renders the real parchment mesh unlit into a PRIVATE RT and the screen quad
        // shows that. (Two other answers were tried here and are gone: mod-built world-space quads
        // textured with the GH_CampaignMap textures — the renderer's world AABB does not match the
        // camera's coordinate space, so the quads were mis-placed; and keeping the game's deferred
        // render while stripping its image effects — the effects were never the cause.)
        ReconcileAlbedoCamera(mapSource);

        bool anyMirrorRendering = false;
        for (int i = 0; i < _mirrors.Count; i++)
        {
            MirrorEntry entry = _mirrors[i];
            // Map (ISSUE 1): both eyes are mono from the base RT, so the per-eye mirrors are
            // pointless here — keep them off (the map albedo camera owns the base RT).
            bool want = entry.SourceOn && !suspend && !shift && !_mapBaseCapture;
            if (entry.Mirror.enabled != want)
                entry.Mirror.enabled = want;
            if (want)
                anyMirrorRendering = true;
        }

        // Map base-capture probe (class doc MAP ALBEDO RENDER): a 3D background camera is
        // actively rendering (mirrors on) but the game's own render may be black — check
        // the base RT and engage the albedo render if so.
        TickBlackProbe(anyMirrorRendering);

        // Once-per-second base-RT center probe — the MAP ALBEDO success/failure readout.
        TickAlbedoProbe();
    }

    /// <summary>
    /// Ensure the left eye's shifted intermediate RT exists and matches the left
    /// RT's dimensions (the right RT is kept in step by <see cref="Tick"/>). A
    /// creation failure latches <see cref="_shiftRtFailed"/> for this activation —
    /// the caller falls back to the plain suspension.
    /// </summary>
    private bool EnsureShiftRt()
    {
        if (_leftRt == null || _rtRight == null || _shiftRtFailed)
            return false;
        if (_rtLeftShifted != null
            && (_rtLeftShifted.width != _leftRt.width || _rtLeftShifted.height != _leftRt.height))
        {
            ReleaseShiftRt();
        }
        if (_rtLeftShifted == null)
        {
            var rt = CreateColorRt(_leftRt.width, _leftRt.height, 0, "GloomhavenVR.FlatScreenRT.LeftShifted");
            if (!rt.Create())
            {
                Object.Destroy(rt);
                _shiftRtFailed = true;
                VRLog.Warn("WorldUI", "Video depth shift: left-shifted RT creation failed — " +
                                      "plain suspension fallback (both eyes = left RT; never " +
                                      "one-eyed, never black).");
                return false;
            }
            _rtLeftShifted = rt;
        }
        return true;
    }

}

using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    // ---- camera ownership policies (docs/CAMERA-POLICY.md) --------------------------------

    /// <summary>
    /// Head culling mask policy (docs/CAMERA-POLICY.md §2):
    ///
    /// - SCENARIO rig: anchor game camera's mask OR the mod layer bit, never 0 — the
    ///   head camera renders the diorama world plus mod visuals.
    /// - MENU rig (hardware test #10 fix): the MOD LAYER ONLY — nothing else, ever.
    ///   Menu2D shows the world exclusively THROUGH the FlatScreen RT composite; the
    ///   anchor mask on the campaign map (0xF00FFE37, the whole 3D world) rendered the
    ///   giant map 1:1 below the player while the quad showed on top of it. The HMD in
    ///   Menu2D must contain exactly: void + screen quad + hands + indicator.
    ///
    /// Cheap per-frame re-assert — the game may rewrite the anchor's mask (and
    /// CanvasConversion may OR UI bits onto our camera in scenario); the policy must
    /// survive every foreign write.
    /// </summary>
    private static int ComposeHeadMask(int sourceMask) =>
        DropOptOutLayers((sourceMask == 0 ? 1 : sourceMask) | VRLayers.ModLayerMask);

    /// <summary>
    /// Subtract <c>[Optimize] HeadCullingMaskDrop</c> (2026-07 submission-cost pass). Empty by
    /// default, so this is the identity until a tester names a layer — and the mod layer is
    /// protected unconditionally, because dropping it would take the hands, the control board and
    /// the settings panel with it and leave no way back inside the headset.
    /// </summary>
    private static int DropOptOutLayers(int mask)
    {
        int drop = Core.PerfConfig.HeadMaskDropMask & ~VRLayers.ModLayerMask;
        return drop == 0 ? mask : mask & ~drop;
    }

    /// <summary>
    /// The game's own scenario renderer, resolved by name and cached. Probed on the same cadence
    /// as the camera-policy sweep and only while unresolved, because <c>Camera.allCameras</c>
    /// allocates an array on every read. A Unity-null here (scene unloaded the camera) simply
    /// re-arms the probe.
    /// </summary>
    private Camera? _scenarioCam;
    private int _scenarioCamProbe;

    /// <summary>
    /// Layers the scenario mask narrowing must NEVER drop, whatever the ScenarioCamera's own mask
    /// says. The mod layer carries every mod visual (hands, cards, control board, panels) and the
    /// built-in UI layer carries the game's own uGUI, which <c>CanvasConversion</c> re-parents into
    /// world space and which the flat ScenarioCamera therefore never had to render — its mask
    /// excludes layer 5 on hardware. Dropping either would blank the headset with no way back
    /// inside it, which is exactly the failure mode a "safe by default" switch must not have.
    /// </summary>
    private static int MaskNarrowingFloor => VRLayers.ModLayerMask | (1 << 5);

    /// <summary>Last mask actually written, so the change is logged once and not per frame.</summary>
    private int _loggedHeadMask;

    private Camera? ResolveScenarioCamera()
    {
        if (_scenarioCam != null)
            return _scenarioCam;
        if (--_scenarioCamProbe > 0)
            return null;
        _scenarioCamProbe = SweepIntervalFrames;
        Camera[] all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "ScenarioCamera")
            {
                _scenarioCam = all[i];
                break;
            }
        }
        return _scenarioCam;
    }

    /// <summary>
    /// Say, once per distinct mask, exactly which layers the head camera stopped rendering and
    /// what was on them. A narrowing that makes something invisible has to be diagnosable from the
    /// log alone — the tester is inside a headset and cannot inspect a bitmask there.
    /// </summary>
    private void LogHeadMaskChange(int wanted, int source, bool narrowed)
    {
        if (wanted == _loggedHeadMask)
            return;
        _loggedHeadMask = wanted;
        if (!narrowed)
            return;
        var sb = new System.Text.StringBuilder(256);
        sb.Append("Head culling mask narrowed to the ScenarioCamera's ([Optimize] ")
          .Append("HeadMaskFromScenarioCamera): 0x").Append(source.ToString("X8"))
          .Append(" → 0x").Append(wanted.ToString("X8")).Append(". No longer rendered:");
        int dropped = _anchor != null ? _anchor.cullingMask & ~wanted : 0;
        bool any = false;
        for (int layer = 0; layer < 32; layer++)
        {
            if ((dropped & (1 << layer)) == 0)
                continue;
            string name = LayerMask.LayerToName(layer);
            sb.Append(any ? ", " : " ").Append(layer).Append('=')
              .Append(string.IsNullOrEmpty(name) ? "<unnamed>" : name);
            any = true;
        }
        if (!any)
            sb.Append(" nothing (the ScenarioCamera's mask was no narrower than the anchor's)");
        sb.Append(". The [Perf] SCENE line's per-layer counts say how many renderers that removes; "
                  + "if something you need went invisible, switch the entry back off.");
        VRLog.Info("Rig", sb.ToString());
    }

    private void TickHeadCullingMask()
    {
        if (_kind == RigKind.None || _camera == null)
            return;
        int wanted;
        if (_kind == RigKind.Menu)
        {
            // Mod layer only — never follow the anchor in Menu2D (test #10). The opt-out list is
            // deliberately NOT applied here: the menu mask is already the minimum that keeps the
            // HMD usable, and it contains only the mod layer, which the drop can never remove.
            wanted = VRLayers.ModLayerMask;
        }
        else
        {
            // Follow the live anchor mask while the anchor exists (the game may toggle
            // layers scene-side); once the anchor died, keep re-asserting our own.
            int source = _anchor != null ? _anchor.cullingMask : _camera.cullingMask;

            // 2026-07 submission-cost pass, opt-in: the scenario ANCHOR is 'Main Camera' and its
            // mask is 0xFFFFFFFF — every layer — while the camera the flat game actually renders
            // the dungeon with excludes thirteen of them. Following the anchor therefore makes the
            // head camera cull and submit a surplus the game never draws, twice per frame under
            // MultiPass. Seeding from the ScenarioCamera instead removes exactly that surplus,
            // with the mod layer and the UI layer added back unconditionally (see the floor).
            bool narrowed = false;
            if (PerfConfig.HeadMaskFromScenarioCam)
            {
                Camera? scenario = ResolveScenarioCamera();
                if (scenario != null && scenario.cullingMask != 0)
                {
                    source = scenario.cullingMask | MaskNarrowingFloor;
                    narrowed = true;
                }
            }
            wanted = ComposeHeadMask(source);
            LogHeadMaskChange(wanted, source, narrowed);
        }
        if (_camera.cullingMask != wanted)
            _camera.cullingMask = wanted;
    }

    /// <summary>
    /// Re-assert <c>[Optimize] HeadDepthPrepass</c> onto the head camera's
    /// <see cref="Camera.depthTextureMode"/> (2026-07 submission-cost pass; one enum compare per
    /// frame, same enforcement pattern as the MSAA/clip-plane re-asserts above).
    ///
    /// <para>WHY IT IS A LEVER AT ALL. On the built-in FORWARD path — which
    /// <see cref="CreateHeadCamera"/> selects — <see cref="DepthTextureMode.Depth"/> is not a flag
    /// the engine reads off some buffer it already has. Forward has no G-buffer, so Unity BUILDS
    /// <c>_CameraDepthTexture</c> by rendering the whole opaque scene AGAIN through each shader's
    /// shadow-caster pass. That is a second full scene submission per eye pass: four per frame
    /// under MultiPass where the mod's own head camera already costs 15–17 ms of main-thread
    /// cull+submit. It is the largest single piece of submission volume the mod adds, and the
    /// 2026-07 measurement says submission volume is the wall.</para>
    ///
    /// <para>WHY IT IS NOT SIMPLY REMOVED. The depth texture is what makes the game's VFX shaders
    /// soft-fade against geometry; without it the fade fails OPEN and torch glow renders through
    /// thin walls — the exact bug <see cref="CreateHeadCamera"/>'s comment records fixing. So the
    /// default is today's behaviour and the switch exists to be MEASURED, from the Debug pane,
    /// with the measurement window closed on the boundary.</para>
    ///
    /// <para>WHY THIS TOUCHES ONE BIT AND NOT THE WHOLE FIELD. <c>depthTextureMode</c> is a FLAGS
    /// field that any image effect on the camera may OR its own requirement into (<c>DepthNormals</c>,
    /// <c>MotionVectors</c>), and writing it forces the engine to reconfigure the camera's render
    /// targets. Asserting a whole VALUE would therefore fight any such component for the field and
    /// rewrite it EVERY FRAME — at 3072×3264 per eye that is a continuous target reconfiguration,
    /// which is a stall, not a lever. The per-frame re-assert pattern that is correct for
    /// <c>antiAliasing</c> (the game rewrites it on every quality-level swap) would be actively
    /// harmful here. So this owns exactly the <see cref="DepthTextureMode.Depth"/> BIT, leaves
    /// every other bit alone, and in the default state performs no write at all — the value
    /// <see cref="CreateHeadCamera"/> already set is the value it wants.</para>
    /// </summary>
    private void TickDepthTextureMode()
    {
        if (_camera == null)
            return;
        DepthTextureMode current = _camera.depthTextureMode;
        bool wantDepth = Core.PerfConfig.DepthPrepassOn;
        if (wantDepth == ((current & DepthTextureMode.Depth) != 0))
            return;
        _camera.depthTextureMode = wantDepth
            ? current | DepthTextureMode.Depth
            : current & ~DepthTextureMode.Depth;
    }

    /// <summary>
    /// Keep the owned head camera's SolidColor clear on <c>[Rig] VoidColor</c> —
    /// live-tunable (per-frame color compare only; Skybox-clear anchors keep their sky).
    /// </summary>
    private void TickHeadClearColor()
    {
        if (_camera == null || _camera.clearFlags != CameraClearFlags.SolidColor)
            return;
        Color wanted = Plugin.VoidColor.Value;
        if (_camera.backgroundColor != wanted)
            _camera.backgroundColor = wanted;
    }

    /// <summary>
    /// Keep the owned head camera's clip planes scale-aware (test #17: hands
    /// vanished at max zoom-in — WorldGrab shrinks the rig scale, the hands' world-
    /// unit distance from the eyes shrinks with it, and the build-time near plane
    /// clipped them). near = <see cref="BaseNearMeters"/> × live rig scale, clamped
    /// to absolute world-unit bounds; far grows with zoom-out (the eyes recede from
    /// the fixed-size world) but never drops below the anchor-derived build value,
    /// with the far/near ratio capped for depth precision. Menu rig: scale stays 1,
    /// so this degenerates to the build values. Two float compares per frame.
    ///
    /// While a [Sky] 3D environment is spawned (SkyAlternative), the far plane
    /// additionally never drops below the environment's real-size view distance
    /// (<see cref="Core.SkyAlternative.MinFarWorldUnits"/> — its star dome sits at
    /// authored-meters × rig scale world units FROM ITS WORLD-ANCHORED ORIGIN, plus
    /// the head's distance to that origin now that locomotion can carry the player
    /// away from it); 0 while idle, so this degenerates to the old value.
    /// </summary>
    private void TickClipPlanes()
    {
        if (_camera == null || _rigRoot == null)
            return;
        float scale = _rigRoot.transform.localScale.x;
        float near = Mathf.Clamp(BaseNearMeters * scale, MinNearClip, MaxNearClip);
        float far = Mathf.Min(
            Mathf.Max(Mathf.Max(_baseFarClip, _baseFarClip * (scale / _buildScale)),
                      SkyAlternative.MinFarWorldUnits(scale)),
            near * MaxFarNearRatio);
        if (!Mathf.Approximately(_camera.nearClipPlane, near))
            _camera.nearClipPlane = near;
        if (!Mathf.Approximately(_camera.farClipPlane, far))
            _camera.farClipPlane = far;
    }

    /// <summary>
    /// Stereo-exclusion pump: sweep immediately on scene loads (new foreign cameras,
    /// e.g. MainMenu's stereo=Both 'Main Camera'), otherwise on a frame cadence that
    /// also catches cameras created mid-scene. Rig rebuilds sweep inside Build*.
    /// </summary>
    private void TickCameraPolicy(bool sceneLoaded)
    {
        if (!VRSession.IsRunning)
            return;
        if (sceneLoaded)
        {
            VRCameraPolicy.PruneDead();
            MixedReality.PruneDead(); // drop MR bookkeeping for cameras the unload destroyed
            VRCameraPolicy.Sweep("scene load");
            _sweepCountdown = SweepIntervalFrames;
            return;
        }
        if (--_sweepCountdown > 0)
            return;
        _sweepCountdown = SweepIntervalFrames;
        VRCameraPolicy.Sweep("periodic");
    }

    // ---- owned head camera -----------------------------------------------------------------

    /// <summary>
    /// Create OUR head camera under the rig root, seeded from the anchor game camera:
    /// depth = anchor + 1, far plane from the anchor. Clip planes are seeded for
    /// <paramref name="rigScale"/> and kept scale-aware per frame by
    /// <see cref="TickClipPlanes"/> (test #17). Mask policy is <see cref="ComposeHeadMask"/>'s:
    /// scenario = anchor mask | mod layer (never 0); menu (<paramref name="modLayerOnly"/>,
    /// test #10) = the mod layer ONLY, with a forced SolidColor [Rig] VoidColor clear.
    /// Scenario keeps the anchor's Skybox clear when it has one (that IS
    /// visible content). The game camera itself is
    /// never modified; stereo on it (and every other game camera) is owned by
    /// <see cref="VRCameraPolicy"/>.
    /// </summary>
    private void CreateHeadCamera(Camera anchor, float rigScale, bool modLayerOnly = false)
    {
        _cameraGo = new GameObject("GloomhavenVR.HeadCamera");
        _cameraGo.transform.SetParent(_rigRoot!.transform, worldPositionStays: false);
        _cameraGo.transform.localPosition = Vector3.zero;
        _cameraGo.transform.localRotation = Quaternion.identity;

        _buildScale = rigScale;
        _baseFarClip = Mathf.Max(anchor.farClipPlane, 100f);

        _camera = _cameraGo.AddComponent<Camera>();
        _camera.cullingMask = modLayerOnly ? VRLayers.ModLayerMask : ComposeHeadMask(anchor.cullingMask);
        _camera.depth = anchor.depth + 1f;
        _camera.nearClipPlane = Mathf.Clamp(BaseNearMeters * rigScale, MinNearClip, MaxNearClip);
        _camera.farClipPlane = _baseFarClip;
        _camera.allowHDR = anchor.allowHDR;
        // ALWAYS allow MSAA on the head camera (aliasing fix #5b/#6): the game's cameras may
        // ship allowMSAA=false and copying that would silently veto the [RenderQuality]
        // MsaaLevel eye-texture MSAA. allowMSAA is only a permission — actual sampling is
        // QualitySettings.antiAliasing (RenderQuality.Tick) and only on the forward path;
        // on deferred it is ignored, so forcing it on is always safe.
        _camera.allowMSAA = true;
        _camera.useOcclusionCulling = anchor.useOcclusionCulling;
        if (!modLayerOnly && anchor.clearFlags == CameraClearFlags.Skybox)
        {
            _camera.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Plugin.VoidColor.Value; // [Rig] VoidColor, default black
        }
        // FOV is owned by the XR display (per-eye projection) — no need to copy.
        _camera.stereoTargetEye = StereoTargetEyeMask.Both;

        // OCCLUSION ROOT CAUSE (transparent effects through walls — flames, hex ring, health bars):
        // the SkyBackdrop DepthResetRenderer (Overlay shader, ZTest Always, queue 1001) resets depth
        // to far so the near sky sphere doesn't occlude the floated board/menus. The Overlay shader
        // has NO deferred pass, so on a DEFERRED camera it renders in the forward-opaque FALLBACK —
        // AFTER the deferred G-buffer walls — and its ZTest-Always wipes the wall depth for the whole
        // transparent pass, so every transparent effect (queue 3000-4000, even ZTest LEqual like the
        // patched hex ring) draws over walls. Opaque figures are unaffected (occluded in the G-buffer
        // BEFORE the wipe) — which is exactly the observed split. FORWARD rendering restores strict
        // per-queue order: the reset (1001) runs BEFORE the walls (1900+), the walls overwrite it, the
        // depth buffer keeps the walls, and transparents occlude correctly (the reset's original
        // design assumption). Config-gated so forward's per-object light limit can be reverted if the
        // dungeon lighting regresses.
        if (Plugin.ForwardRendering.Value)
            _camera.renderingPath = RenderingPath.Forward;

        // OCCLUSION (the fire/glow-through-walls saga, final root cause): the game's VFX shaders
        // (torch/candle flames+glow, DFade clouds, distortion) SOFT-FADE against
        // _CameraDepthTexture — big glow billboards physically poke through thin walls, and the
        // depth-fade term is what hides those poked-through fragments in the flat game (its camera
        // gets the depth texture via the game's own stack, incl. the PostProcessLayer the mod
        // kill-switches). Our mod-created head camera shipped with DepthTextureMode.None, so the
        // fade sampled nothing and FAILED OPEN → glow rendered fully through walls. All serialized
        // shader pass states were proven clean (ZTest LEqual, walls ZWrite On) — the ONLY missing
        // piece was this depth texture. One extra depth prepass per eye is the cost; the visual
        // result is the game's ORIGINAL intended soft-particle look.
        //
        // COST, MEASURED 2026-07: on the forward path this is not a free flag — forward has no
        // G-buffer, so Unity builds the texture by re-rendering every opaque object through its
        // shadow-caster pass, a full extra scene submission PER EYE. See TickDepthTextureMode,
        // which owns the value from here on and lets [Optimize] HeadDepthPrepass A/B it.
        _camera.depthTextureMode = Core.PerfConfig.DepthPrepassOn
            ? DepthTextureMode.Depth
            : DepthTextureMode.None;

        // We drive the pose via TrackedPoseDriver — switch off the implicit XR camera
        // tracking the display subsystem would otherwise apply on top.
        XRDevice.DisableAutoXRCameraTracking(_camera, true);

        _poseDriver = _cameraGo.AddComponent<TrackedPoseDriver>();
        _poseDriver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        _poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        _poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        VRCameraPolicy.AllowedHead = _camera;
    }

    /// <summary>Rig root: DontDestroyOnLoad (scene swaps must not kill our camera) + hidden.</summary>
    private GameObject CreateRigRoot()
    {
        var root = new GameObject("GloomhavenVR.VRRig");
        Object.DontDestroyOnLoad(root);
        root.hideFlags = HideFlags.HideAndDontSave;
        return root;
    }
}

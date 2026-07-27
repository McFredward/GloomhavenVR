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
        (sourceMask == 0 ? 1 : sourceMask) | VRLayers.ModLayerMask;

    private void TickHeadCullingMask()
    {
        if (_kind == RigKind.None || _camera == null)
            return;
        int wanted;
        if (_kind == RigKind.Menu)
        {
            // Mod layer only — never follow the anchor in Menu2D (test #10).
            wanted = VRLayers.ModLayerMask;
        }
        else
        {
            // Follow the live anchor mask while the anchor exists (the game may toggle
            // layers scene-side); once the anchor died, keep re-asserting our own.
            int source = _anchor != null ? _anchor.cullingMask : _camera.cullingMask;
            wanted = ComposeHeadMask(source);
        }
        if (_camera.cullingMask != wanted)
            _camera.cullingMask = wanted;
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
    /// </summary>
    private void TickClipPlanes()
    {
        if (_camera == null || _rigRoot == null)
            return;
        float scale = _rigRoot.transform.localScale.x;
        float near = Mathf.Clamp(BaseNearMeters * scale, MinNearClip, MaxNearClip);
        float far = Mathf.Min(
            Mathf.Max(_baseFarClip, _baseFarClip * (scale / _buildScale)),
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
    /// <see cref="TickClipPlanes"/> (test #17). Mask policy (CAMERA-POLICY §2):
    /// scenario = anchor mask | mod layer (never 0); menu (<paramref name="modLayerOnly"/>,
    /// test #10) = the mod layer ONLY, with a forced SolidColor [Rig] VoidColor clear —
    /// Menu2D shows the world exclusively through the FlatScreen RT, so the HMD renders
    /// void + quad + hands and nothing of the 3D scene. Scenario keeps the anchor's
    /// Skybox clear when it has one (that IS visible content). The game camera itself is
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
        // the SkyBackdrop DepthResetRenderer (Overlay shader, ZTest Always, queue 1999) resets depth
        // to far so the near sky sphere doesn't occlude the floated board/menus. The Overlay shader
        // has NO deferred pass, so on a DEFERRED camera it renders in the forward-opaque FALLBACK —
        // AFTER the deferred G-buffer walls — and its ZTest-Always wipes the wall depth for the whole
        // transparent pass, so every transparent effect (queue 3000-4000, even ZTest LEqual like the
        // patched hex ring) draws over walls. Opaque figures are unaffected (occluded in the G-buffer
        // BEFORE the wipe) — which is exactly the observed split. FORWARD rendering restores strict
        // per-queue order: the reset (1999) runs BEFORE the walls (2000), the walls overwrite it, the
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
        _camera.depthTextureMode = DepthTextureMode.Depth;

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

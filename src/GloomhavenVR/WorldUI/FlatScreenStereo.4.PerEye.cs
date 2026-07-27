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
    /// <summary>Destroy all mirrors (captured stack released — scene change / hide). Cheap to rebuild.</summary>
    internal void ReleaseMirrors()
    {
        for (int i = _mirrors.Count - 1; i >= 0; i--)
            DestroyMirrorAt(i);
        _mirrors.Clear();
        _bySource.Clear();

        // Re-arm map albedo render for the next scene (class doc MAP ALBEDO RENDER): the
        // evidence (a black base RT) belongs to the scene we just left, and the worldMap
        // renderer/overrides reference textures from it. A late probe callback is
        // invalidated by the generation bump.
        if (_mapBaseCapture || _blackConsecutive != 0)
        {
            _mapBaseCapture = false;
            _blackConsecutive = 0;
            _probeGen++;
            _probePending = false;
            _albedoProbePending = false;
            ReleaseAlbedo();
        }
    }

    private void DestroyMirrorAt(int index)
    {
        MirrorEntry entry = _mirrors[index];
        // Unity fake-null: the managed key survives Destroy — Remove still works.
        _bySource.Remove(entry.Source);
        if (entry.Go != null)
            Object.Destroy(entry.Go);
        _mirrors.RemoveAt(index);
    }

    private MirrorEntry CreateMirror(Camera source)
    {
        if (_root == null)
        {
            _root = new GameObject("GloomhavenVR.StereoScreenMirrors");
            Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;
        }

        var go = new GameObject("GloomhavenVR.StereoMirror." + source.name);
        go.transform.SetParent(_root.transform, worldPositionStays: false);

        var cam = go.AddComponent<Camera>();
        cam.enabled = false;                              // EndStackSync flips it on
        cam.stereoTargetEye = StereoTargetEyeMask.None;   // VRCameraPolicy-invisible by construction
        cam.targetTexture = _rtRight;
        XRDevice.DisableAutoXRCameraTracking(cam, true);

        var entry = new MirrorEntry
        {
            Source = source,
            Mirror = cam,
            MirrorTransform = go.transform,
            Go = go,
            // Camera-hosted VideoPlayer (MainMenuVideo ambient movies, campaign
            // 'Video Camera' fullscreen videos) — first shift probe; players added
            // later or bound from another GO via targetCamera (the intro) are
            // caught by the throttled recheck/sweep (class doc VIDEO DISCOVERY).
            Video = source.GetComponent<VideoPlayer>(),
        };
        _mirrors.Add(entry);
        _bySource.Add(source, entry);

        VRLog.Info("WorldUI", $"Stereo mirror created for '{source.name}': eye offset " +
                              $"{_sepScene:F4} scene units, converge {_convScene:F2}" +
                              $"{(entry.Video != null ? ", hosts a VideoPlayer (video depth shift applies)" : "")}.");
        return entry;
    }

    /// <summary>
    /// True while this player blits camera-plane frames into the SOURCE camera's
    /// output. The binding check (targetCamera or same GO) lets a player that gets
    /// re-targeted elsewhere stop shifting stereo without waiting for a sweep.
    /// </summary>
    private static bool IsNearPlaneVideoActive(VideoPlayer? video, Camera source) =>
        video != null && video.enabled
        && (video.renderMode == VideoRenderMode.CameraNearPlane
            || video.renderMode == VideoRenderMode.CameraFarPlane)
        && (video.targetCamera == source || video.gameObject == source.gameObject);

    // ---- per-eye quad texture (MultiPass) ----------------------------------------------------

    /// <summary>
    /// Head-camera pre-render hook: swap the quad material's texture per eye pass.
    /// MultiPass renders the head camera twice per frame with
    /// <c>stereoActiveEye</c> = Left/Right; if a runtime ever reports Mono instead,
    /// a per-frame pass-parity fallback (first pass = Left) takes over seamlessly.
    /// While the video depth shift is engaged, the first pass of each frame also
    /// refreshes BOTH per-eye shifted copies of the left RT (class doc VIDEO DEPTH
    /// SHIFT — the freshest possible copy, one pair of blits per frame).
    /// Nothing is restored afterwards — the desktop mirror blits the left RT
    /// directly and every pass re-asserts its own texture.
    /// ALSO: for the mod albedo camera (campaign map), this is where the unlit worldMap
    /// material override is swapped ON (restored in <see cref="OnPostRenderCamera"/>).
    /// </summary>
    /// <summary>
    /// Apply the captured game-map view+projection to the mirror camera BEFORE its culling runs. Unity
    /// culls using the camera's matrices at onPreCull time; if we only fixed them in onPreRender (after
    /// culling), the map quads were culled OUT against the stale grazing matrices → nothing drawn → black.
    /// </summary>
    private void OnPreCullCamera(Camera cam)
    {
        if (!_active)
            return;
        // Drive the real game map camera to our computed pose right before it culls/renders, so the game's
        // on-map marker projection and click raycasts (both via CameraController.m_Camera) align with our
        // render. Safe: the VR rig uses m_Camera only as a build-time anchor and never follows its transform.
        if (MapDriveGameCamera && _mapBaseCapture && _mapDrivenValid && _mapSourceCam != null && cam == _mapSourceCam)
        {
            cam.transform.SetPositionAndRotation(_mapDrivenPos, Quaternion.LookRotation((_mapDrivenLook - _mapDrivenPos).normalized, Vector3.up));
            cam.fieldOfView = _mapDrivenFov;
            cam.orthographic = false;
        }
        if (_mapAlbedoCam != null && cam == _mapAlbedoCam && _capMapValid)
        {
            cam.worldToCameraMatrix = _capMapView;
            cam.projectionMatrix = _capMapProj;
        }
        if (_mapAlbedoCam != null && cam == _mapAlbedoCam)
            DrawMapIcons(cam); // queue location icon quads right before our camera renders (correct timing)
    }

    private void OnPreRenderCamera(Camera cam)
    {
        if (!_active)
            return;
        // Capture the GAME map camera's ACTUAL render-time view+projection (the CameraController sets them
        // at render time, so the values read in Tick are stale/grazing — the map ends up off-frustum). The
        // game map camera (depth -1) renders BEFORE our mirror (-0.9), so these are ready when the mirror runs.
        if (!MapDiagTopDown && _mapBaseCapture && _mapSourceCam != null && cam == _mapSourceCam)
        {
            _capMapView = cam.worldToCameraMatrix;
            _capMapProj = cam.projectionMatrix;
            _capMapValid = true;
            return;
        }
        if (_mapAlbedoCam != null && cam == _mapAlbedoCam)
        {
            // Our forward camera: re-apply the game camera's render-time matrices so the REAL mesh (drawn
            // at its true GPU vertex positions) projects exactly like the game's map (pan/zoom tracked);
            // fall back to the Tick-time copy if not captured. Then swap the MapUnlit override onto the
            // parchment renderer for exactly this render (restored in OnPostRenderCamera).
            if (_capMapValid)
            {
                cam.worldToCameraMatrix = _capMapView;
                cam.projectionMatrix = _capMapProj;
                if (!_capLogged)
                {
                    _capLogged = true;
                    VRLog.Info("WorldUI", "MAP RENDER render-time matrices applied to the forward camera " +
                                          "(captured from the game map camera's OnPreRender) — the real mesh tracks pan/zoom.");
                }
            }
            // DIAGNOSTIC (once): project the parchment mesh's 8 local-bounds corners through the applied
            // MVP and log their NDC extent. If x/y span most of [-1,1] the mesh fills the screen (full UV
            // visible); if it clusters tiny, the framing is zoomed/degenerate (near-constant UV → flat) —
            // which, given the mesh/texture/UV are proven-correct offline, would be the real bug.
            // Log at engage AND periodically for a while (the map has an open/zoom animation — the
            // settled pose differs from the first frame). Up to ~6 samples, one every 90 frames.
            if (_ndcLogCount < 6 && _worldMapRenderer != null
                && (_ndcLogCount == 0 || Time.frameCount - _ndcLastLogFrame >= 90))
            {
                _ndcLogCount++;
                _ndcLastLogFrame = Time.frameCount;
                Matrix4x4 mvp = cam.projectionMatrix * cam.worldToCameraMatrix * _worldMapRenderer.localToWorldMatrix;
                Vector3 mn = _mapLocalMin, sz = _mapLocalSize;
                float nxMin = 1e9f, nxMax = -1e9f, nyMin = 1e9f, nyMax = -1e9f, nzMin = 1e9f, nzMax = -1e9f;
                int behind = 0;
                for (int c = 0; c < 8; c++)
                {
                    var lp = new Vector3(mn.x + ((c & 1) != 0 ? sz.x : 0f),
                                         mn.y + ((c & 2) != 0 ? sz.y : 0f),
                                         mn.z + ((c & 4) != 0 ? sz.z : 0f));
                    Vector4 clip = mvp * new Vector4(lp.x, lp.y, lp.z, 1f);
                    if (clip.w <= 0f) { behind++; continue; }
                    float nx = clip.x / clip.w, ny = clip.y / clip.w, nz = clip.z / clip.w;
                    nxMin = Mathf.Min(nxMin, nx); nxMax = Mathf.Max(nxMax, nx);
                    nyMin = Mathf.Min(nyMin, ny); nyMax = Mathf.Max(nyMax, ny);
                    nzMin = Mathf.Min(nzMin, nz); nzMax = Mathf.Max(nzMax, nz);
                }
                VRLog.Info("WorldUI", $"MAP RENDER NDC extent [sample {_ndcLogCount} f{Time.frameCount}]: x[{nxMin:F2},{nxMax:F2}] y[{nyMin:F2},{nyMax:F2}] " +
                                      $"z[{nzMin:F2},{nzMax:F2}] cornersBehind={behind}/8 " +
                                      $"(localMin={_mapLocalMin} size={_mapLocalSize}, ortho={cam.orthographic}, " +
                                      $"l2w.scale=({_worldMapRenderer.localToWorldMatrix.lossyScale}); full-screen ⇒ x,y span ~[-1,1]).");
                // Geometry dump to explain the grazing/off-screen projection: where the game map camera
                // sits and points vs where the parchment mesh actually is in world space.
                Vector3 camPos = cam.worldToCameraMatrix.inverse.GetColumn(3);
                Vector3 camFwd = -(Vector3)cam.worldToCameraMatrix.GetRow(2); // -Z row = forward in world
                Transform rt = _worldMapRenderer.transform;
                Vector3 meshCenterWorld = _worldMapRenderer.bounds.center;
                Vector3 toMesh = (meshCenterWorld - camPos);
                float distToMesh = toMesh.magnitude;
                float facing = Vector3.Dot(camFwd, toMesh.normalized);
                Camera? src = _mapSourceCam;
                VRLog.Info("WorldUI", $"MAP RENDER geometry [sample {_ndcLogCount}]: camPos={camPos} camFwd={camFwd}; " +
                                      $"meshTransform pos={rt.position} euler={rt.eulerAngles} lossyScale={rt.lossyScale}; " +
                                      $"rendererBoundsCenter={meshCenterWorld} rendererBoundsSize={_worldMapRenderer.bounds.size}; " +
                                      $"cam→meshCenter dist={distToMesh:F2} facingDot={facing:F3} (1=dead ahead, <0=behind); " +
                                      $"srcCam='{(src != null ? src.name : "null")}' srcPos={(src != null ? src.transform.position.ToString() : "?")} " +
                                      $"srcEuler={(src != null ? src.transform.eulerAngles.ToString() : "?")} srcFOV={(src != null ? src.fieldOfView : 0f):F1} " +
                                      $"srcOrtho={(src != null ? src.orthographic : false)} srcNear={(src != null ? src.nearClipPlane : 0f):F2} srcFar={(src != null ? src.farClipPlane : 0f):F1}.");
                if (src != null)
                {
                    float planeY = _worldMapRenderer.bounds.center.y;
                    Vector3 vpCenter = src.WorldToViewportPoint(meshCenterWorld);
                    bool okC = TryMapPlaneHit(src, 0.5f, 0.5f, planeY, out Vector3 fCenter);
                    bool okBL = TryMapPlaneHit(src, 0f, 0f, planeY, out Vector3 fBL);
                    bool okTR = TryMapPlaneHit(src, 1f, 1f, planeY, out Vector3 fTR);
                    bool okB = TryMapPlaneHit(src, 0.5f, 0f, planeY, out Vector3 fB);
                    bool okT = TryMapPlaneHit(src, 0.5f, 1f, planeY, out Vector3 fT);
                    float vExtent = (okB && okT) ? (fT - fB).magnitude : -1f;
                    VRLog.Info("WorldUI", $"MAP RENDER game-cam footprint: WorldToViewport(meshCenter)={vpCenter} " +
                                          $"(on-screen ⇒ x,y in [0,1] & z>0); planeY={planeY:F2}; " +
                                          $"footprintCenter={(okC ? fCenter.ToString() : "MISS")} " +
                                          $"BL={(okBL ? fBL.ToString() : "MISS")} TR={(okTR ? fTR.ToString() : "MISS")} " +
                                          $"vExtent={vExtent:F2} (map is 190×237; small vExtent ⇒ zoomed region).");
                }
            }
            ApplyWorldMapOverride();
            return;
        }
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null || cam != head)
            return;
        Material? mat = _quadMaterial;
        if (mat == null)
            return;

        if (_videoShift && _shiftBlitFrame != Time.frameCount
            && _leftRt != null && _rtRight != null && _rtLeftShifted != null)
        {
            _shiftBlitFrame = Time.frameCount;
            // Behind-the-screen (uncrossed) disparity displaces each eye's IMAGE
            // toward that eye — left −x, right +x — and a blit offset moves the
            // SAMPLING window, i.e. the negative of the image shift, on top of the
            // centered overscan margin.
            float zoom = 1f / VideoOverscan;
            float margin = (1f - zoom) * 0.5f;
            float shift = _videoShiftUv;
            var scale = new Vector2(zoom, zoom);
            RenderTexture? previous = RenderTexture.active;
            Graphics.Blit(_leftRt, _rtLeftShifted, scale, new Vector2(margin + shift, margin));
            Graphics.Blit(_leftRt, _rtRight, scale, new Vector2(margin - shift, margin));
            RenderTexture.active = previous;
        }

        Camera.MonoOrStereoscopicEye eye = cam.stereoActiveEye;
        bool right;
        if (eye == Camera.MonoOrStereoscopicEye.Right)
        {
            right = true;
        }
        else if (eye == Camera.MonoOrStereoscopicEye.Left)
        {
            right = false;
        }
        else
        {
            // Mono reported (unexpected for the stereo head) — pass-parity fallback.
            if (_parityFrame != Time.frameCount)
            {
                _parityFrame = Time.frameCount;
                _parityIndex = 0;
            }
            right = (_parityIndex & 1) == 1;
            _parityIndex++;
        }

        // One-time diagnostic (per activation): the observed eye-pass pattern proves
        // in the log whether stereoActiveEye is trustworthy on this runtime.
        if (_eyeObsCount < 2)
        {
            if (_eyeObsCount == 0) _eyeObs0 = eye; else _eyeObs1 = eye;
            _eyeObsCount++;
            if (_eyeObsCount == 2)
                VRLog.Info("WorldUI", $"Stereo screen eye passes observed: {_eyeObs0}, {_eyeObs1} — " +
                                      (_eyeObs0 == Camera.MonoOrStereoscopicEye.Left
                                       && _eyeObs1 == Camera.MonoOrStereoscopicEye.Right
                                          ? "stereoActiveEye drives the per-eye texture swap."
                                          : "using pass-parity fallback where Mono is reported."));
        }

        // Target per eye: suspension (intro guard / video-unavailable / campaign-map albedo)
        // → the left RT for BOTH eyes (for the map it now holds the mod's bright parchment
        // render); depth shift → the per-eye shifted copies; otherwise the mirror-rendered
        // right RT and, for the left eye, the left RT.
        // #2: while a scenario/story/encounter overlay is up, do NOT show the map behind it — paint the
        // background BLACK so the overlay fills the screen (atmosphere) and no map is visible at the edges.
        // Map capture stays engaged (no re-detect thrash); it simply isn't shown until the overlay closes.
        if (_mapBaseCapture && IsScenarioOverlayActive())
        {
            if (!ReferenceEquals(mat.mainTexture, Texture2D.blackTexture))
                mat.mainTexture = Texture2D.blackTexture;
            return;
        }

        RenderTexture? target;
        // Map (ISSUE 1): the map is not suspended (the split keeps routing so the UI glass survives),
        // but it IS mono — force both eyes onto the PRIVATE map RT that carries the mod's forward render
        // (falling back to the base RT if the private RT is unavailable).
        if (_mapBaseCapture && _mapRt != null)
            target = _mapRt;
        else if (_videoSuspended || _mapBaseCapture)
            target = _leftRt;
        else if (_videoShift && _rtLeftShifted != null && _rtRight != null)
            target = right ? _rtRight : _rtLeftShifted;
        else if (right)
            target = _rtRight != null ? _rtRight : _leftRt;
        else
            target = _leftRt;
        if (target != null && !ReferenceEquals(mat.mainTexture, target))
            mat.mainTexture = target;
    }

    /// <summary>
    /// Post-render hook: restore the parchment renderer's original materials right after our forward map
    /// camera finishes drawing it (the MapUnlit override is scoped to exactly that one render).
    /// </summary>
    private void OnPostRenderCamera(Camera cam)
    {
        if (_mapAlbedoCam != null && cam == _mapAlbedoCam)
            RestoreWorldMapOverride();
    }

    // ---- IPD -------------------------------------------------------------------------------

    /// <summary>
    /// Sample the real eye separation from the XR head device every ~90 frames
    /// (allocation-free feature reads). Plausibility-clamped; falls back to 63 mm
    /// when the runtime does not expose per-eye positions.
    /// </summary>
    private void SampleIpd()
    {
        if (Time.frameCount - _ipdFrame < IpdSampleIntervalFrames && _ipdFrame != int.MinValue)
            return;
        _ipdFrame = Time.frameCount;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (device.isValid
            && device.TryGetFeatureValue(CommonUsages.leftEyePosition, out Vector3 left)
            && device.TryGetFeatureValue(CommonUsages.rightEyePosition, out Vector3 right))
        {
            float ipd = Vector3.Distance(left, right);
            if (ipd > 0.045f && ipd < 0.08f)
            {
                if (!_ipdLogged)
                {
                    _ipdLogged = true;
                    VRLog.Info("WorldUI", $"Stereo screen IPD from HMD: {ipd * 1000f:F1} mm.");
                }
                _ipdMeters = ipd;
                return;
            }
        }
        if (!_ipdLogged)
        {
            _ipdLogged = true;
            VRLog.Info("WorldUI", "Stereo screen IPD not exposed by the runtime — using the " +
                                  $"{DefaultIpdMeters * 1000f:F0} mm default.");
        }
    }
}

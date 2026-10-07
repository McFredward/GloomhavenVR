using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    // ---- ITEM 9: desktop (flat monitor) = clean LEFT-EYE mirror ---------------------------

    private sealed class DesktopDisplayRecord
    {
        internal UnityEngine.XR.XRDisplaySubsystem Display = null!;
        internal int Original;
        internal int Applied;
    }

    /// <summary>
    /// User clarification 2026-10-02: Off means a black spectator backbuffer, never
    /// restoration of native flat scenery/UI. The shipped built-in OpenXR player
    /// consumes the display provider's mirror mode; the legacy XRSettings selector
    /// has no None value. Own both selectors reversibly, and suppress provider blits
    /// with XRMirrorViewBlitMode.None when no desktop image was requested. The same
    /// provider preference is used by SRP; camera hooks below also cover that path.
    /// A readback is evidence of the requested mode, not proof of compositor pixels.
    /// </summary>
    private void TickDesktopMirrorMode()
    {
        if (!VRSession.IsRunning)
        {
            RestoreDesktopMirrorMode();
            return;
        }
        bool leftEye = DesktopMirrorLeftEye;
        if (!_mirrorModeCaptured)
        {
            _originalMirrorMode = UnityEngine.XR.XRSettings.gameViewRenderMode;
            _mirrorModeCaptured = true;
        }
        if (leftEye)
        {
            if (UnityEngine.XR.XRSettings.gameViewRenderMode != UnityEngine.XR.GameViewRenderMode.LeftEye)
                UnityEngine.XR.XRSettings.gameViewRenderMode = UnityEngine.XR.GameViewRenderMode.LeftEye;
            _legacyMirrorApplied = true;
        }
        _mirrorModeApplied = true;

        UnityEngine.SubsystemManager.GetInstances(_desktopDisplays);
        for (int i = _desktopDisplayRecords.Count - 1; i >= 0; i--)
            if (!_desktopDisplays.Contains(_desktopDisplayRecords[i].Display))
                _desktopDisplayRecords.RemoveAt(i); // provider teardown cannot retain a stale ownership record
        int desired = leftEye ? UnityEngine.XR.XRMirrorViewBlitMode.LeftEye : UnityEngine.XR.XRMirrorViewBlitMode.None;
        for (int i = 0; i < _desktopDisplays.Count; i++)
        {
            UnityEngine.XR.XRDisplaySubsystem display = _desktopDisplays[i];
            if (!display.running)
                continue;
            try
            {
                DesktopDisplayRecord? record = null;
                for (int r = 0; r < _desktopDisplayRecords.Count; r++)
                    if (ReferenceEquals(_desktopDisplayRecords[r].Display, display))
                    {
                        record = _desktopDisplayRecords[r];
                        break;
                    }
                int current = display.GetPreferredMirrorBlitMode();
                if (record == null)
                {
                    record = new DesktopDisplayRecord { Display = display, Original = current, Applied = current };
                    _desktopDisplayRecords.Add(record);
                }
                if (current != desired)
                    display.SetPreferredMirrorBlitMode(desired);
                record.Applied = desired;
                if (display.GetPreferredMirrorBlitMode() != desired && !_desktopMirrorWarning)
                {
                    _desktopMirrorWarning = true;
                    VRLog.Warn("WorldUI", "Desktop mirror provider did not retain the requested mode; " +
                        "native desktop draws remain suppressed and the Off backbuffer is cleared black.");
                }
            }
            catch (System.Exception e)
            {
                if (!_desktopMirrorWarning)
                {
                    _desktopMirrorWarning = true;
                    VRLog.Warn("WorldUI", $"Desktop mirror provider mode unavailable: {e.Message}. " +
                        "Native desktop draws remain suppressed; Off clears the backbuffer black.");
                }
            }
        }
        if (!_mirrorModeLogged || _mirrorModeLastLeftEye != leftEye)
        {
            _mirrorModeLogged = true;
            _mirrorModeLastLeftEye = leftEye;
            VRLog.Info("WorldUI", "ITEM9 desktop mirror mode = " + (leftEye ? "LEFT EYE" : "BLACK") +
                $": provider request={desired}, XRSettings.gameViewRenderMode={UnityEngine.XR.XRSettings.gameViewRenderMode}; " +
                "unused native desktop drawing is always suppressed; no desktop 2D-menu composite. " +
                (leftEye ? "Requested HMD LEFT-eye spectator image." : "Requested black spectator image."));
        }
    }

    /// <summary>Restore captured spectator preferences on VR stop or hot reload only.</summary>
    private void RestoreDesktopMirrorMode()
    {
        if (!_mirrorModeApplied)
            return;
        for (int i = 0; i < _desktopDisplayRecords.Count; i++)
        {
            DesktopDisplayRecord record = _desktopDisplayRecords[i];
            try
            {
                if (record.Display.GetPreferredMirrorBlitMode() == record.Applied)
                    record.Display.SetPreferredMirrorBlitMode(record.Original);
            }
            catch (System.Exception) { /* A destroyed provider cannot be restored. */ }
        }
        _desktopDisplayRecords.Clear();
        _desktopDisplays.Clear();
        if (_mirrorModeCaptured && _legacyMirrorApplied
            && UnityEngine.XR.XRSettings.gameViewRenderMode == UnityEngine.XR.GameViewRenderMode.LeftEye)
            UnityEngine.XR.XRSettings.gameViewRenderMode = _originalMirrorMode;
        _legacyMirrorApplied = false;
        _mirrorModeApplied = false;
        _mirrorModeCaptured = false;
        _mirrorModeLogged = false;
        _desktopMirrorWarning = false;
        VRLog.Info("WorldUI", $"ITEM9 desktop mirror mode restored to {_originalMirrorMode} (VR off / hot reload).");
    }

    /// <summary>
    /// ITEM 9 (SCENARIO STATE) — keep the game's OWN cameras off the desktop backbuffer
    /// while the flat screen is HIDDEN.
    ///
    /// WHY THE DESKTOP STILL SHOWED EXTRA UI: <see cref="Core.VRCameraPolicy"/> forces
    /// every game camera to <c>StereoTargetEyeMask.None</c> while VR runs (only the rig
    /// head camera renders the HMD). A None camera renders to the DESKTOP backbuffer.
    /// In Menu2D <see cref="CaptureStack"/> redirects all of them into the flat-screen RT,
    /// so nothing but the XR left-eye mirror reaches the monitor — but in a SCENARIO the
    /// flat screen is hidden, CaptureStack does not run, and the game's flat 2D UI (hand,
    /// bars, buttons) and 3D board composite straight onto the monitor IN PARALLEL with
    /// the mirror. That is the "extra UI on the desktop" this item reports; it is NOT the
    /// XR runtime's mirror (which only ever carries the HMD eye) — it is the game's own
    /// desktop cameras drawing next to it.
    ///
    /// FIX (mirrors CaptureStack): retarget EVERY non-head backbuffer camera onto one
    /// throwaway offscreen sink RT (Screen-sized, so Camera pixel dimensions and
    /// screen-space raycasts are unchanged) so ONLY the XR mirror composites onto the
    /// monitor. The rig <see cref="Rig.VRRigDriver.HeadCamera"/> (the sole stereo renderer
    /// → HMD) is excluded, so the in-VR view is untouched. Native callbacks, camera identity
    /// and projection remain live; draw skip below suppresses the discarded pixels. The
    /// headset draws converted original widgets itself and never samples this sink. Fully reversible (targetTexture → null) on
    /// VR stop / hot reload / when the flat screen shows (CaptureStack owns
    /// the cameras then). [WorldUI] DesktopMirrorLeftEye controls only the spectator image.
    ///
    /// The native 2D menu is still captured when
    /// FlatScreen is visible; only its otherwise-unused desktop render is skipped.
    /// Instrumented: the full backbuffer inventory is logged once per scene so the next
    /// hardware log names exactly which cameras reached the desktop and what was scrubbed.
    /// </summary>
    private void TickDesktopCameraScrub()
    {
        // Only while the flat screen is HIDDEN: Menu2D already routes every backbuffer
        // camera into its own RT via CaptureStack (double-owning them would fight).
        bool want = FrameDesktopPolicy.ScrubGameCameras(
            DesktopMirrorLeftEye, VRSession.IsRunning, _visible);
        if (!want)
        {
            ReleaseDesktopScrub(_visible ? "flat screen captures the cameras" : "VR stopped");
            return;
        }

        if (_scrubRt == null)
        {
            _scrubRt = new RenderTexture(Mathf.Max(Screen.width, 1280), Mathf.Max(Screen.height, 720), 24)
            {
                name = "GloomhavenVR.DesktopScrubSink",
                antiAliasing = 1,
            };
            _scrubRt.Create();
            DesktopScrubTarget = _scrubRt;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        int count = Core.VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);

        string scene = SceneManager.GetActiveScene().name;
        bool logInventory = _scrubInventoryScene != scene;
        if (logInventory)
        {
            _scrubInventoryScene = scene;
            VRLog.Info("WorldUI", $"ITEM9 desktop backbuffer inventory (scene '{scene}', flat screen HIDDEN, " +
                                  $"{count} active camera(s)) — each targetTexture==null camera below composites " +
                                  "onto the monitor; all except the VR head are retargeted to an offscreen sink:");
        }

        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null)
                continue;
            bool isHead = head != null && cam == head;

            if (logInventory)
            {
                Rect r = cam.rect;
                string dest = cam.targetTexture != null ? cam.targetTexture.name : "backbuffer";
                VRLog.Info("WorldUI", $"  '{cam.name}' tag={cam.tag} enabled={cam.enabled} depth={cam.depth:F1} " +
                                      $"clear={cam.clearFlags} rect=({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2}) " +
                                      $"mask=0x{cam.cullingMask:X8} stereo={cam.stereoTargetEye} target={dest}" +
                                      (isHead ? " [VR head — KEPT: headset output, spectator image follows its setting]"
                                       : cam.targetTexture != null ? " [own RT — left alone]"
                                       : " [SCRUBBED → offscreen sink]"));
            }

            if (isHead || FlatScreen.IsCaptured(cam))
                continue; // head renders the HMD; captured cameras belong to CaptureStack
            if (cam.targetTexture == _scrubRt)
                continue; // already scrubbed
            if (cam.targetTexture != null)
                continue; // renders to its own RT (e.g. 'GUI 3D Camera') — not the desktop

            cam.targetTexture = _scrubRt;
            if (!_scrubbed.Contains(cam))
                _scrubbed.Add(cam);
            if (!logInventory)
                VRLog.Info("WorldUI", $"ITEM9 desktop scrub: '{cam.name}' (depth {cam.depth:F1}, " +
                                      $"mask 0x{cam.cullingMask:X8}, clear {cam.clearFlags}) retargeted off the " +
                                      "backbuffer to the offscreen sink (native callbacks/projection retained; " +
                                      "discarded scene/UI drawing skipped).");
        }

        // Re-assert (game code may reset targetTexture to null) + compact dead / released.
        for (int i = _scrubbed.Count - 1; i >= 0; i--)
        {
            Camera cam = _scrubbed[i];
            if (cam == null)
            {
                _scrubbed.RemoveAt(i);
                continue;
            }
            if ((head != null && cam == head) || FlatScreen.IsCaptured(cam))
            {
                // Ownership moved (rig rebuilt onto it / flat screen captured it) — drop our claim.
                if (cam.targetTexture == _scrubRt)
                    cam.targetTexture = null;
                _scrubbed.RemoveAt(i);
                continue;
            }
            if (cam.targetTexture == null)
                cam.targetTexture = _scrubRt;          // re-assert our redirect
            else if (cam.targetTexture != _scrubRt)
                _scrubbed.RemoveAt(i);                  // game gave it its own RT — no longer our concern
        }

        _scrubActive = true;
        SyncScrubDrawSkip();
        NativeCameraRenderBudget.Apply(_scrubbed, PerfConfig.UnusedCamerasSuspended);
    }

    // ---- stop PAYING for the scrubbed render (unconditional) ---------------------------------

    /// <summary>
    /// Follows <see cref="_scrubActive"/>: while the scrub holds game cameras on the sink RT, those
    /// cameras' draw is skipped. Always — there is no setting for this and no state in which the
    /// discarded render is wanted.
    ///
    /// <para>WHY: the scrub keeps the game's cameras off the monitor by giving them a sink RT — but
    /// they would still RENDER into it, every frame, at desktop resolution, and in a scenario that
    /// includes the game's scenario camera drawing the whole 3D board. Nothing ever reads the sink
    /// (<c>_scrubRt</c> is never sampled by a material, never blitted, never read back — the field
    /// is private to this class and its only uses are the assignments and comparisons in this
    /// file), so that would be a third full scene render, on top of the two eye passes, for a
    /// texture that is discarded. It renders at DESKTOP resolution, so no eye-resolution or MSAA
    /// lever can touch it — the cost is constant across every quality preset.
    /// <see cref="Core.PerfFrameSplit"/>'s per-camera breakdown prices what is saved.</para>
    ///
    /// <para>WHY IT IS SAFE: the skip was carried as an opt-in switch through 2026-07 and verified
    /// on hardware across repeated scenario sessions with nothing visibly or functionally different
    /// — expected, because the cameras keep every property game code can observe (see below) and
    /// the only thing lost is pixels in a texture with no reader. The switch was removed once that
    /// was settled; do not reintroduce one.</para>
    ///
    /// <para>LEGACY FALLBACK, RETAINED WITH THE STRONGER BUDGET OFF: <c>cam.enabled = false</c> looks obvious and is
    /// breaks unbridged native readers. <see cref="Camera.main"/> only returns ENABLED cameras tagged MainCamera, the
    /// scenario camera carries that tag, and both game code and ~20 mod fallbacks resolve through
    /// Camera.main — disabling it would NRE them. Zeroing the culling mask for the duration of the
    /// camera's own render keeps the camera enabled, keeps Camera.main, keeps pixel dimensions and
    /// screen-space projection, still clears, and still runs any image effects; it just draws
    /// nothing. Everything else in the process — including the rig's per-frame
    /// <c>anchor.cullingMask</c> follow, which runs in Update/LateUpdate, before rendering — reads
    /// the untouched value. Build612 optionally suspends the discarded cameras after the
    /// managed projection bridge is prepared; failed bridge setup keeps this fallback.</para>
    ///
    /// <para>Restore is belt-and-braces and load-bearing: the mask is zeroed in
    /// <see cref="OnScrubPreCull"/> and put back in <see cref="OnScrubPostRender"/>, i.e. within
    /// the same camera's own render, so no other code ever observes a zeroed mask.
    /// <see cref="OnScrubPreCull"/> also restores any slot still occupied first, so a render that
    /// never reaches post-render (camera disabled mid-frame, exception in an image effect) cannot
    /// leave a game camera blinded, and <see cref="ReleaseDesktopScrub"/> restores on the way out.
    /// Every mutation here must stay reversible.</para>
    /// </summary>
    private void SyncScrubDrawSkip()
    {
        if (_scrubActive == _scrubDrawSkipHooked)
            return;
        if (_scrubActive)
        {
            Camera.onPreCull += OnScrubPreCull;
            Camera.onPostRender += OnScrubPostRender;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnScrubBeginCameraRendering;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnScrubEndCameraRendering;
            _scrubDrawSkipHooked = true;
            VRLog.Info("WorldUI", "ITEM9 desktop scrub: DRAW SKIP engaged — the redirected game cameras " +
                                  "still clear and still run image effects; unused skyboxes are skipped and their culling mask is zeroed " +
                                  "for the duration of their own render, so the full 3D scene is no longer " +
                                  "drawn into a sink nothing reads.");
        }
        else
        {
            Camera.onPreCull -= OnScrubPreCull;
            Camera.onPostRender -= OnScrubPostRender;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnScrubBeginCameraRendering;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnScrubEndCameraRendering;
            _scrubDrawSkipHooked = false;
            RestoreScrubMask();
            VRLog.Info("WorldUI", "ITEM9 desktop scrub: draw skip released — the cameras are going back to " +
                                  "the backbuffer, so they render their full culling mask again.");
        }
    }

    private void OnScrubBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            OnScrubPreCull(cam);
    }

    private void OnScrubEndCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            OnScrubPostRender(cam);
    }

    private void OnScrubPreCull(Camera cam)
    {
        RestoreScrubMask(); // a previous camera never reached post-render — never leave one blinded
        if (_scrubRt == null || cam == null || cam.targetTexture != _scrubRt || !_scrubbed.Contains(cam))
            return;
        _maskedCam = cam;
        _maskedValue = cam.cullingMask;
        _maskedClearFlags = cam.clearFlags;
        _maskedSkybox = _maskedClearFlags == CameraClearFlags.Skybox;
        // A skybox is drawn outside the culling mask, so zero alone still pays
        // for discarded background geometry. Own only this camera's render clear
        // and restore it with the mask; native menus/previews are never admitted.
        if (_maskedSkybox)
            cam.clearFlags = CameraClearFlags.SolidColor;
        cam.cullingMask = 0;
    }

    private void OnScrubPostRender(Camera cam)
    {
        if (_maskedCam != null && cam == _maskedCam)
            RestoreScrubMask();
    }

    private void RestoreScrubMask()
    {
        if (_maskedCam == null)
            return;
        _maskedCam.cullingMask = _maskedValue;
        if (_maskedSkybox && _maskedCam.clearFlags == CameraClearFlags.SolidColor)
            _maskedCam.clearFlags = _maskedClearFlags;
        _maskedSkybox = false;
        _maskedCam = null;
    }

    /// <summary>
    /// Restore every scrubbed camera to the backbuffer and drop the sink RT
    /// (VR off / hot reload / the flat screen taking the cameras over).
    /// </summary>
    private void ReleaseDesktopScrub(string reason)
    {
        NativeCameraRenderBudget.Restore(); // Camera identity must be live before native capture.
        if (!_scrubActive && _scrubbed.Count == 0 && _scrubRt == null)
            return;
        bool wasActive = _scrubActive;
        _scrubActive = false;   // so SyncScrubDrawSkip unhooks + un-blinds before we let go
        SyncScrubDrawSkip();
        int restored = 0;
        for (int i = 0; i < _scrubbed.Count; i++)
        {
            Camera cam = _scrubbed[i];
            if (cam == null)
                continue;
            if (cam.targetTexture == _scrubRt)
            {
                cam.targetTexture = null;
                restored++;
            }
        }
        _scrubbed.Clear();
        if (_scrubRt != null)
        {
            if (DesktopScrubTarget == _scrubRt)
                DesktopScrubTarget = null;
            _scrubRt.Release();
            Object.Destroy(_scrubRt);
            _scrubRt = null;
        }
        if (wasActive)
            VRLog.Info("WorldUI", $"ITEM9 desktop scrub released ({reason}) — {restored} camera(s) restored to the backbuffer.");
        _scrubInventoryScene = null;
    }

}

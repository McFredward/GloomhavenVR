using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    // ---- ITEM 9: desktop (flat monitor) = clean LEFT-EYE mirror ---------------------------

    /// <summary>
    /// ITEM 9 — force the flat monitor to mirror ONLY the HMD's LEFT eye. Unity/OpenXR's
    /// default game-view mirror mode is uncontrolled (which eye — or both, side by side —
    /// is build/driver dependent), so pin
    /// <see cref="UnityEngine.XR.XRSettings.gameViewRenderMode"/> to
    /// <c>GameViewRenderMode.LeftEye</c>. This is a real RUNTIME property (not an
    /// editor-only field) — valid on a built OpenXR player — and controls the desktop
    /// mirror-view blit Unity performs after every frame. It is re-asserted every tick
    /// because a scene load or game code can rewrite it, and the original is captured
    /// once so VR-off / hot-reload / the toggle restores it (<see cref="RestoreDesktopMirrorMode"/>).
    /// Combined with the <see cref="OnEndOfFrame"/> composite gate, the desktop shows the
    /// rig head camera's LEFT eye and nothing else — the left-eye mirror already carries
    /// the in-VR flat-screen quad (which shows the 2D menu), so no separate 2D composite
    /// is needed and the monitor never goes black.
    /// </summary>
    private void TickDesktopMirrorMode()
    {
        if (!DesktopMirrorLeftEye || !VRSession.IsRunning)
        {
            RestoreDesktopMirrorMode();
            return;
        }
        if (!_mirrorModeCaptured)
        {
            _originalMirrorMode = UnityEngine.XR.XRSettings.gameViewRenderMode;
            _mirrorModeCaptured = true;
        }
        if (UnityEngine.XR.XRSettings.gameViewRenderMode != UnityEngine.XR.GameViewRenderMode.LeftEye)
            UnityEngine.XR.XRSettings.gameViewRenderMode = UnityEngine.XR.GameViewRenderMode.LeftEye;
        _mirrorModeApplied = true;
        if (!_mirrorModeLogged)
        {
            _mirrorModeLogged = true;
            // Read the value BACK: on some OpenXR runtimes gameViewRenderMode is a no-op
            // (the compositor owns the mirror), so this line tells the next hardware log
            // whether the managed set is honored — and, if not, that the desktop-camera
            // scrub below is the only lever that keeps the monitor clean.
            var readback = UnityEngine.XR.XRSettings.gameViewRenderMode;
            bool honored = readback == UnityEngine.XR.GameViewRenderMode.LeftEye;
            VRLog.Info("WorldUI", "ITEM9 desktop mirror mode = LEFT EYE: XRSettings.gameViewRenderMode " +
                                  $"set to LeftEye (was {_originalMirrorMode}); readback={readback} " +
                                  (honored
                                      ? "(runtime HONORED — the mirror carries the left eye only). "
                                      : "(runtime IGNORED — the OpenXR compositor's mirror is not controllable " +
                                        "from managed code here; the desktop-camera scrub keeps the monitor clean instead). ") +
                                  "The end-of-frame 2D-menu composite blit is skipped.");
        }
    }

    /// <summary>Restore the original game-view mirror mode (VR off / toggle off / hot reload).</summary>
    private void RestoreDesktopMirrorMode()
    {
        if (!_mirrorModeApplied)
            return;
        if (_mirrorModeCaptured)
            UnityEngine.XR.XRSettings.gameViewRenderMode = _originalMirrorMode;
        _mirrorModeApplied = false;
        _mirrorModeLogged = false;
        VRLog.Info("WorldUI", $"ITEM9 desktop mirror mode restored to {_originalMirrorMode} (VR off / toggle off / hot reload).");
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
    /// → HMD) is excluded, so the in-VR view is untouched; the game UI still RENDERS (into
    /// the sink), so the WorldUI canvas conversion that consumes it in VR is unaffected —
    /// only its DESKTOP output is suppressed. Fully reversible (targetTexture → null) on
    /// VR stop / toggle off / hot reload / when the flat screen shows (CaptureStack owns
    /// the cameras then). Gated by [WorldUI] DesktopMirrorLeftEye (off = legacy).
    ///
    /// Instrumented: the full backbuffer inventory is logged once per scene so the next
    /// hardware log names exactly which cameras reached the desktop and what was scrubbed.
    /// </summary>
    private void TickDesktopCameraScrub()
    {
        // Only while the flat screen is HIDDEN: Menu2D already routes every backbuffer
        // camera into its own RT via CaptureStack (double-owning them would fight).
        bool want = DesktopMirrorLeftEye && VRSession.IsRunning && !_visible;
        if (!want)
        {
            ReleaseDesktopScrub(_visible ? "flat screen captures the cameras" : "toggle off / VR stopped");
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
                                      (isHead ? " [VR head — KEPT: its XR mirror IS the desktop]"
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
                                      "backbuffer to the offscreen sink (kept off the monitor; still renders for " +
                                      "the in-VR canvas conversion).");
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
    }

    // ---- optional: stop PAYING for the scrubbed render ---------------------------------------

    /// <summary>
    /// The scrub keeps the game's cameras off the monitor by giving them a sink RT — but they
    /// still RENDER into it, every frame, at desktop resolution, and in a scenario that includes
    /// the game's scenario camera drawing the whole 3D board. Nothing ever reads the sink
    /// (<c>_scrubRt</c> is never sampled by a material, never blitted, never read back), so on
    /// top of the two eye passes the GPU is doing a third full scene render for a texture that is
    /// discarded.
    ///
    /// <para>WHAT THE 2026-07 MEASUREMENT ACTUALLY SAYS ABOUT IT. The original note here cited the
    /// runtime's <c>gpu</c> figure as evidence that this render matters. That figure turned out to
    /// be unusable — it read the frame INTERVAL in every window and did not move when the
    /// pixel-sample budget was cut 11×, because the runtime had locked the app to 45 Hz. So this
    /// remains an untested suspect, but a well-motivated one for a reason the failed experiment
    /// could not touch: it renders at DESKTOP resolution, so the eye-resolution and MSAA levers
    /// cannot affect it at all, which is exactly the shape of a cost that stays constant across
    /// every quality preset. <see cref="Core.PerfFrameSplit"/>'s per-camera breakdown prices it
    /// directly, and toggling the switch marks an A/B boundary in the log.</para>
    ///
    /// <para>WHY CULLING RATHER THAN DISABLING: <c>cam.enabled = false</c> looks obvious and is
    /// wrong here. <see cref="Camera.main"/> only returns ENABLED cameras tagged MainCamera, the
    /// scenario camera carries that tag, and both game code and ~20 mod fallbacks resolve through
    /// Camera.main — disabling it would NRE them. Zeroing the culling mask for the duration of the
    /// camera's own render keeps the camera enabled, keeps Camera.main, keeps pixel dimensions and
    /// screen-space projection, still clears, and still runs any image effects; it just draws
    /// nothing. Everything else in the process — including the rig's per-frame
    /// <c>anchor.cullingMask</c> follow, which runs in Update/LateUpdate, before rendering — reads
    /// the untouched value.</para>
    ///
    /// <para>Restore is belt-and-braces: <see cref="OnScrubPostRender"/> is the normal path, and
    /// <see cref="OnScrubPreCull"/> restores any slot still occupied first, so a render that never
    /// reaches post-render (camera disabled mid-frame, exception in an image effect) cannot leave
    /// a game camera blinded. <see cref="ReleaseDesktopScrub"/> restores on the way out.</para>
    /// </summary>
    private void SyncScrubDrawSkip()
    {
        bool want = _scrubActive && WorldUIConfig.SkipDesktopScrubDraw.Value;
        if (want == _scrubDrawSkipHooked)
            return;
        if (want)
        {
            Camera.onPreCull += OnScrubPreCull;
            Camera.onPostRender += OnScrubPostRender;
            _scrubDrawSkipHooked = true;
            VRLog.Info("WorldUI", "ITEM9 desktop scrub: DRAW SKIP engaged ([WorldUI] SkipDesktopScrubDraw) " +
                                  "— the redirected game cameras still clear and still run image effects, " +
                                  "but their culling mask is zeroed for the duration of their own render, " +
                                  "so the full 3D scene is no longer drawn into a sink nothing reads. " +
                                  "READ THE RESULT ON THE [Perf] SPLIT LINE, not on the FRAME line's 'gpu' " +
                                  "figure: that counter reads the frame INTERVAL whenever the runtime is " +
                                  "rate-locked, which is most of the time here, so it cannot see this. SPLIT " +
                                  "prices every camera's own cull+submit separately — the scenario and UI " +
                                  "cameras should collapse toward zero while this is on — and the toggle " +
                                  "closes the measurement window on both sides, so the A/B is two adjacent " +
                                  "SPLIT lines with nothing straddling the change.");
        }
        else
        {
            Camera.onPreCull -= OnScrubPreCull;
            Camera.onPostRender -= OnScrubPostRender;
            _scrubDrawSkipHooked = false;
            RestoreScrubMask();
            VRLog.Info("WorldUI", "ITEM9 desktop scrub: draw skip released — the redirected cameras render " +
                                  "their full culling mask into the sink again.");
        }
    }

    private void OnScrubPreCull(Camera cam)
    {
        RestoreScrubMask(); // a previous camera never reached post-render — never leave one blinded
        if (_scrubRt == null || cam == null || cam.targetTexture != _scrubRt || !_scrubbed.Contains(cam))
            return;
        _maskedCam = cam;
        _maskedValue = cam.cullingMask;
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
        _maskedCam = null;
    }

    /// <summary>
    /// Restore every scrubbed camera to the backbuffer and drop the sink RT
    /// (VR off / toggle off / hot reload / the flat screen taking the cameras over).
    /// </summary>
    private void ReleaseDesktopScrub(string reason)
    {
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
            _scrubRt.Release();
            Object.Destroy(_scrubRt);
            _scrubRt = null;
        }
        if (wasActive)
            VRLog.Info("WorldUI", $"ITEM9 desktop scrub released ({reason}) — {restored} camera(s) restored to the backbuffer.");
        _scrubInventoryScene = null;
    }

}

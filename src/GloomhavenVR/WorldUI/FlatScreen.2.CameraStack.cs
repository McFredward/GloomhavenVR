using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    /// <summary>
    /// Redirect every backbuffer game camera into our RTs (I1, hardware test #5) —
    /// UI-classified cameras onto the glass RT while the split routes, everything
    /// else onto the background RT — and keep each stack's lowest-depth camera on
    /// its forced SolidColor clear (opaque for the background, transparent for the
    /// UI; P3b — see field comments). Cheap per-tick re-assert: game code may
    /// rewrite targetTexture or clearFlags at any time, and new cameras
    /// (MainMenuVideo) appear mid-scene. Excluded: our rig head camera and cameras
    /// already targeting a different RT ('GUI 3D Camera' → character-assembly RT,
    /// per the test-#5 camera inventory). Originals are recorded on first capture
    /// and restored by <see cref="ReleaseStack"/>. No per-frame allocations: shared
    /// scan buffer + reused list; records allocate only when a new camera is first
    /// captured.
    /// </summary>
    private void CaptureStack()
    {
        if (_rt == null)
            return;

        TickSplitLifecycle();

        Camera? head = Rig.VRRigDriver.HeadCamera;

        // 1. Capture new backbuffer cameras / re-assert the redirect on known ones.
        int count = Core.VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || (head != null && cam == head))
                continue;
            if (CapturedSet.TryGetValue(cam, out CapturedCamera known))
            {
                RenderTexture? want = TargetFor(known);
                if (want != null && cam.targetTexture != want) // game code rewrote it — re-assert
                    cam.targetTexture = want;
                continue;
            }
            if (cam.targetTexture != null)
                continue; // renders to its own RT (e.g. 'GUI 3D Camera') — not ours

            var record = new CapturedCamera
            {
                Camera = cam,
                OriginalClearFlags = cam.clearFlags,
                OriginalBackground = cam.backgroundColor,
                WasEnabled = true, // Camera.allCameras only lists enabled cameras
                IsUi = IsUiCamera(cam),
            };
            _captured.Add(record);
            CapturedSet.Add(cam, record);
            cam.targetTexture = TargetFor(record);
            // A UI camera exists after all — the no-UI watchdog's evidence is void
            // (test #20): re-arm so TickSplitLifecycle re-engages on the next sweep.
            if (record.IsUi && _splitNoUi)
            {
                _splitNoUi = false;
                VRLog.Info("WorldUI", $"Screen layer split no-UI fallback re-armed — UI camera " +
                                      $"'{cam.name}' captured; the split re-engages on the next sweep.");
            }
            // Full disposition line (test #10): rect + clear + depth + mask make the RT
            // composite reconstructable from the log alone.
            Rect r = cam.rect;
            VRLog.Info("WorldUI", $"FlatScreen stack capture: '{cam.name}' → RenderTexture " +
                                  $"(depth {cam.depth:F1}, clear {record.OriginalClearFlags} " +
                                  $"'{record.OriginalBackground}', rect ({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2}), " +
                                  $"mask 0x{cam.cullingMask:X8}, {(record.IsUi ? "UI" : "background")} layer).");
        }

        // 2. Compact dead entries (scene unloads destroy cameras behind our back).
        for (int i = _captured.Count - 1; i >= 0; i--)
        {
            if (_captured[i].Camera == null)
            {
                if (_captured[i] == _base)
                    _base = null;
                if (_captured[i] == _uiBase)
                    _uiBase = null;
                CapturedSet.Remove(_captured[i].Camera);
                _captured.RemoveAt(i);
            }
        }

        // 3. Per-stack base selection + clear policy.
        SelectBases();
        TickStackClears();

        // 4. Stereo screen (test #15 #7): mirror the captured 3D cameras into the
        //    right-eye RT — ONLY while the layer split carries the UI on the glass
        //    (mirrors can never render Screen-Space-Camera canvases; test #18:
        //    left-eye-only menu). Runs AFTER the clear policy so mirrors copy the
        //    EFFECTIVE clear flags. Pre-menu scenes engage the stereo intro guard
        //    (test #17 one-eyed intro: the Intro scene's render path is
        //    scene-serialized and unverifiable — FlatScreenStereo forces identical
        //    eyes there, zero shift: the intro must remain verified-identical).
        bool preMenu = IsPreMenuScene();
        if (SplitActive)
        {
            _stereoGateReason = null; // gate open — the diagnostic re-logs on the next block
            _stereo.Tick(_rt, _backRenderer, preMenu);
            if (_stereo.Active)
            {
                _stereo.BeginStackSync();
                for (int i = 0; i < _captured.Count; i++)
                {
                    CapturedCamera c = _captured[i];
                    if (c.Camera != null && !c.IsUi) // UI never mirrors — the glass shows it in both eyes
                        _stereo.SyncCamera(c.Camera);
                }
                _stereo.EndStackSync();
            }

            // Routing follows the suspension decided THIS tick (EndStackSync):
            // suspended (intro guard / unavailable shift path) → the UI rejoins the
            // left RT so the single suspended image carries everything. Applying the flip
            // here is still same-frame — cameras render after Update — so no frame
            // is ever one-eyed or UI-less; bases/clears are recomputed immediately.
            bool routing = !preMenu && !_stereo.Suspended;
            if (routing != _splitRouting)
            {
                SetSplitRouting(routing, routing
                    ? "no suspension in effect"
                    : preMenu ? "pre-menu scene" : "stereo suspended");
                SelectBases();
                TickStackClears();
            }
            TickNoUiWatchdog();
        }
        else
        {
            if (_stereo.Active)
                _stereo.Deactivate("screen layer split inactive");
            // Gate diagnostic (test #20: the flat menu was exactly this state and the
            // log never named it): with the split down, the WHOLE stereo/video-depth
            // chain is structurally off — one change-deduped line names the reason so
            // a flat screen in a hardware log is attributable at a glance.
            // ScreenLayerSplit is a constant since the 2026-08-22 settings audit (off = a one-eyed
            // main menu), so "off (config)" can no longer be the reason — the three MEASURED
            // reasons below are what is left, and they are the ones the diagnostic was written for.
            string gate = _splitFailed ? "split creation failed this Show"
                : _splitNoUi ? "no-UI watchdog latched (re-arms on scene change / UI capture)"
                : "split not engaged (quad/RT not ready)";
            if (gate != _stereoGateReason)
            {
                _stereoGateReason = gate;
                VRLog.Info("WorldUI", $"Stereo screen + video depth layer unavailable — {gate}; " +
                                      "the screen renders the single mono RT.");
            }
        }
    }

    /// <summary>
    /// UI-stack classification (class doc SCREEN LAYER SPLIT): the cameras whose
    /// Screen-Space-Camera canvases hold the 2D UI. UICamera-tagged (CanvasManager
    /// binds every overlay canvas to the first such camera — PATCH-TARGETS §1.7) or
    /// orthographic cameras are UI; everything else is 3D background. Identical to
    /// the pre-split stereo-mirror MONO classification, so the split only MOVES
    /// layers, it never reclassifies a camera.
    /// </summary>
    private static bool IsUiCamera(Camera cam) => cam.CompareTag("UICamera") || cam.orthographic;

    /// <summary>The RT this captured camera should render into under the current routing.</summary>
    private RenderTexture? TargetFor(CapturedCamera c) =>
        c.IsUi && _splitRouting && _uiRt != null ? _uiRt : _rt;

    /// <summary>
    /// Each stack's FIRST camera gets a forced SolidColor clear (fresh RTs have
    /// undefined color — see field comments): opaque black for the background RT,
    /// transparent for the glass RT. Unity renders cameras targeting the same RT in
    /// ascending depth order, so pick the lowest depth per stack. Depth ties exist
    /// (test-#5 inventory: 'Main Camera' and 'UI Camera' both at depth 1.0) — with
    /// routing OFF the UICamera-tagged camera always composites LAST per game intent
    /// (CanvasManager binds all overlay canvases to it), so on a tie it must NOT be
    /// the background base or its forced clear would erase the other cameras' output.
    /// </summary>
    private void SelectBases()
    {
        CapturedCamera? newBase = null;
        CapturedCamera? newUiBase = null;
        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            if (c.IsUi && _splitRouting)
            {
                if (newUiBase == null || c.Camera.depth < newUiBase.Camera.depth)
                    newUiBase = c;
            }
            else if (newBase == null
                || c.Camera.depth < newBase.Camera.depth
                || (c.Camera.depth == newBase.Camera.depth && newBase.Camera.CompareTag("UICamera")))
            {
                newBase = c;
            }
        }

        if (newBase != _base)
        {
            // The previous base (still captured) returns to its own clear flags.
            if (_base != null && _base.Camera != null)
            {
                _base.Camera.clearFlags = _base.OriginalClearFlags;
                _base.Camera.backgroundColor = _base.OriginalBackground;
            }
            _base = newBase;
            if (_base != null)
                VRLog.Info("WorldUI", $"FlatScreen stack base: '{_base.Camera.name}' (depth {_base.Camera.depth:F1}) " +
                                      $"clears the RT (clear {_base.OriginalClearFlags} → SolidColor opaque black).");
        }

        if (newUiBase != _uiBase)
        {
            if (_uiBase != null && _uiBase.Camera != null)
            {
                _uiBase.Camera.clearFlags = _uiBase.OriginalClearFlags;
                _uiBase.Camera.backgroundColor = _uiBase.OriginalBackground;
            }
            _uiBase = newUiBase;
            if (_uiBase != null)
                VRLog.Info("WorldUI", $"FlatScreen UI-stack base: '{_uiBase.Camera.name}' " +
                                      $"(depth {_uiBase.Camera.depth:F1}) clears the glass RT " +
                                      $"(clear {_uiBase.OriginalClearFlags} → SolidColor transparent).");
        }
    }

    // ---- screen layer split (class doc SCREEN LAYER SPLIT) ---------------------------------

    /// <summary>True while the split is engaged (glass RT exists; routing may still be off).</summary>
    private bool SplitActive => _uiRt != null;

    /// <summary>
    /// Engage/disengage the split per config + failure state (called at the top of
    /// every capture sweep, so a live [WorldUI] ScreenLayerSplit flip takes effect on
    /// the next tick). Engaging creates the transparent glass RT, the alpha material
    /// and the background quad; any creation failure latches <see cref="_splitFailed"/>
    /// and the single-RT fallback takes over for the rest of this Show.
    /// </summary>
    private void TickSplitLifecycle()
    {
        bool want = WorldUIConfig.ScreenLayerSplit && !_splitFailed && !_splitNoUi
                    && _rt != null && _quad != null && _quadRenderer != null;
        if (want && SplitActive)
        {
            EnsureBackQuad(); // rebuilt if an external tool destroyed it (same policy as the screen quad)
            return;
        }
        if (!want)
        {
            TeardownSplit("ScreenLayerSplit disabled");
            return;
        }

        // Glass RT: same dimensions as the background RT — the pointer pixel mapping
        // and the quad UVs are shared between the two layers by construction.
        var uiRt = new RenderTexture(_rt!.width, _rt.height, 24)
        {
            name = "GloomhavenVR.FlatScreenRT.UI",
            antiAliasing = 1,
        };
        // Alpha-blended glass shader (both verified shipped — see the Show() shader
        // comment); the RT alpha is exactly what the UI cameras leave behind, which
        // is the point: everything they did not draw stays see-through.
        Shader? glassShader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (glassShader == null || !uiRt.Create())
        {
            uiRt.Release();
            Object.Destroy(uiRt);
            _splitFailed = true;
            VRLog.Warn("WorldUI", "Screen layer split FAILED to engage (" +
                                  (glassShader == null ? "no alpha-blended shader found" : "glass RT creation failed") +
                                  ") — single-RT fallback (mono screen, video suspension; never one-eyed).");
            return;
        }
        _uiRt = uiRt;
        ClearUiRt(); // fresh RT color is undefined — the glass must start fully transparent

        _glassMaterial = new Material(glassShader) { mainTexture = _uiRt };
        EnsureBackQuad();
        _quadRenderer!.sharedMaterial = _glassMaterial;

        _noUiSince = -1f;
        VRLog.Info("WorldUI", $"Screen layer split ENGAGED: UI cameras → transparent glass RT " +
                              $"({_uiRt.width}x{_uiRt.height}) on the screen quad (both eyes identical, " +
                              $"pointer plane); 3D cameras stay on the background RT shown " +
                              $"{BackplaneGapMeters * 100f:F0} cm behind it. glass RT {FlatScreenStereo.DescribeRt(_uiRt)} " +
                              "(default read/write — not sRGB-corrected, so the UI is not overbrightened).");
    }

    /// <summary>Create the background quad — or re-create it after an external destroy.</summary>
    private void EnsureBackQuad()
    {
        if (_backQuad != null)
            return;
        _backQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _backQuad.name = "GloomhavenVR.FlatScreen.Background";
        Object.DontDestroyOnLoad(_backQuad); // survives Single-mode scene loads like the screen quad
        Object.Destroy(_backQuad.GetComponent<Collider>());
        _backRenderer = _backQuad.GetComponent<Renderer>();
        _backRenderer.sharedMaterial = _screenMaterial;
        VRLayers.Apply(_backQuad);
        PlaceScreen(instant: true); // the fresh quad needs its pose now, not on the next head move
    }

    /// <summary>
    /// Fallback path (class doc): give every UI camera the background RT back
    /// (single-RT composite, exactly the pre-split behavior), drop the glass state
    /// and the background quad, and put the opaque single-RT material back on the
    /// screen quad. FlatScreenStereo is deactivated by the next capture sweep
    /// (split inactive → mono — mirrors cannot carry the UI).
    /// </summary>
    private void TeardownSplit(string reason)
    {
        if (!SplitActive)
            return;
        SetSplitRouting(false, reason);
        if (_uiBase != null && _uiBase.Camera != null)
        {
            _uiBase.Camera.clearFlags = _uiBase.OriginalClearFlags;
            _uiBase.Camera.backgroundColor = _uiBase.OriginalBackground;
        }
        _uiBase = null;
        if (_backQuad != null)
        {
            Object.Destroy(_backQuad);
            _backQuad = null;
            _backRenderer = null;
        }
        if (_glassMaterial != null)
        {
            Object.Destroy(_glassMaterial);
            _glassMaterial = null;
        }
        if (_uiRt != null)
        {
            _uiRt.Release();
            Object.Destroy(_uiRt);
            _uiRt = null;
        }
        if (_quadRenderer != null && _screenMaterial != null)
            _quadRenderer.sharedMaterial = _screenMaterial;
        _noUiSince = -1f;
        VRLog.Info("WorldUI", $"Screen layer split RELEASED ({reason}) — single-RT path " +
                              "(all cameras composite the background RT; screen quad opaque).");
    }

    /// <summary>
    /// Flip which RT the UI cameras render (class doc): ON = glass RT (UI floats in
    /// front, both eyes); OFF (stereo suspension / pre-menu) = they rejoin the left
    /// RT so the single suspended image carries everything — the glass is cleared to
    /// fully transparent so no stale UI lingers in front of it.
    /// </summary>
    private void SetSplitRouting(bool routing, string reason)
    {
        if (_splitRouting == routing)
            return;
        _splitRouting = routing;

        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            if (!c.IsUi || c.Camera == null)
                continue;
            RenderTexture? want = TargetFor(c);
            if (want != null && c.Camera.targetTexture != want)
                c.Camera.targetTexture = want;
        }
        if (!routing)
            ClearUiRt();

        VRLog.Info("WorldUI", routing
            ? $"Screen layer split routing ON ({reason}) — UI cameras render the glass RT."
            : $"Screen layer split routing OFF ({reason}) — UI cameras rejoin the background RT " +
              "(glass cleared transparent).");
    }

    /// <summary>Clear the glass RT to fully transparent (engage + every routing-off flip).</summary>
    private void ClearUiRt()
    {
        if (_uiRt == null)
            return;
        RenderTexture? previous = RenderTexture.active;
        RenderTexture.active = _uiRt;
        GL.Clear(clearDepth: true, clearColor: true, backgroundColor: TransparentBlack);
        RenderTexture.active = previous;
    }

    /// <summary>
    /// Split fallback trigger "no UI cameras" (class doc): if the routing stack holds
    /// captured cameras but none classified UI for a sustained period, the canvases'
    /// worldCamera is evidently not one we can retarget — release the split so the
    /// proven single-RT path carries the UI instead of risking a one-eyed screen.
    /// </summary>
    private void TickNoUiWatchdog()
    {
        if (!_splitRouting || _captured.Count == 0)
        {
            _noUiSince = -1f;
            return;
        }
        for (int i = 0; i < _captured.Count; i++)
        {
            if (_captured[i].IsUi)
            {
                _noUiSince = -1f;
                return;
            }
        }
        if (_noUiSince < 0f)
        {
            _noUiSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - _noUiSince < NoUiFallbackSeconds)
            return;

        _splitNoUi = true;
        VRLog.Warn("WorldUI", $"Screen layer split: no UI camera captured for {NoUiFallbackSeconds:F0}s " +
                              "while routing — falling back to the single-RT path (the UI would " +
                              "otherwise be invisible to the stereo mirrors). Re-arms on the next " +
                              "scene change or UI-camera capture (test #20: never latch across scenes).");
        TeardownSplit("no UI cameras found");
    }

    /// <summary>
    /// Per-tick clear-flag policy + enabled-transition diagnostics for the captured
    /// stack (test #10).
    ///
    /// BASE: the stack's lowest-depth camera keeps its forced OPAQUE SolidColor clear
    /// (fresh RTs have undefined color — see field comment).
    ///
    /// NON-BASE FULLSCREEN SolidColor DEMOTION (UNCONDITIONAL since the 2026-08-13 user ruling —
    /// the [WorldUI] DemoteOverlaySolidClears dial is gone, because its OFF let that clear wipe
    /// the composited campaign map to black, i.e. it could switch off the only route into a
    /// scenario from the options menu):
    /// the campaign map's 'Video Camera' (decompiled GH.Runtime/VideoCamera.cs) is a
    /// depth-5 fullscreen-video surface: enabled ONLY between PlayFullscreenVideo
    /// (VideoCamera.cs:96) and EndReached/Stop/error (VideoCamera.cs:146,164,190,
    /// re-enable path :84/:127), renderMode CameraNearPlane (VideoCamera.cs:97), and
    /// its SolidColor clear exists purely as the black BACKDROP behind/between
    /// fullscreen videos. Rendering last into our RT, that clear wipes the whole
    /// composite (map + UI) whenever the near-plane video blit does not land in the
    /// redirected RT — hardware test #10 saw exactly that (black map, black encounter
    /// backgrounds, 'Video Camera' captured at depth 5.0 clear SolidColor). Demoting
    /// the clear to Depth for NON-BASE fullscreen SolidColor cameras diverges from
    /// vanilla only in the backdrop (letterbox bars would show the scene instead of
    /// black while a video plays) and never hides content: the video frame itself, when
    /// it renders, is drawn on top either way. Sub-rect SolidColor cameras keep their
    /// clear — a viewport-limited background IS legitimate visible content
    /// (camera.rect respected). Original flags are restored on release.
    ///
    /// ENABLED TRANSITIONS: Camera.allCameras only lists enabled cameras, so captured
    /// cameras that get disabled (video ended) vanish silently — log every flip so the
    /// next hardware report shows exactly when 'Video Camera' rendered into the RT.
    /// </summary>
    private void TickStackClears()
    {
        for (int i = 0; i < _captured.Count; i++)
        {
            CapturedCamera c = _captured[i];
            Camera cam = c.Camera;
            if (cam == null)
                continue;

            // Enabled-state transition diagnostics.
            bool enabled = cam.isActiveAndEnabled;
            if (enabled != c.WasEnabled)
            {
                c.WasEnabled = enabled;
                Rect r = cam.rect;
                VRLog.Info("WorldUI", $"FlatScreen stack member '{cam.name}' {(enabled ? "ENABLED" : "DISABLED")} " +
                                      $"(depth {cam.depth:F1}, clear {cam.clearFlags}, " +
                                      $"rect ({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2})) — " +
                                      $"{(enabled ? "now renders into" : "no longer renders into")} the RT.");
            }

            if (c == _base)
            {
                // Re-assert the base clear every tick (game code may rewrite it).
                if (cam.clearFlags != CameraClearFlags.SolidColor)
                    cam.clearFlags = CameraClearFlags.SolidColor;
                if (cam.backgroundColor != OpaqueBlack)
                    cam.backgroundColor = OpaqueBlack;
                continue;
            }

            if (c == _uiBase)
            {
                // UI-stack base clears TRANSPARENT — the glass composites over the
                // background layer, so undrawn pixels must stay see-through.
                if (cam.clearFlags != CameraClearFlags.SolidColor)
                    cam.clearFlags = CameraClearFlags.SolidColor;
                if (cam.backgroundColor != TransparentBlack)
                    cam.backgroundColor = TransparentBlack;
                continue;
            }

            if (c.IsUi && _splitRouting)
                continue; // non-base UI cameras keep their own flags (overlay demotion is a BACKGROUND policy)

            bool fullscreen = cam.rect.width >= 0.99f && cam.rect.height >= 0.99f;
            bool wantDemote = fullscreen && c.OriginalClearFlags == CameraClearFlags.SolidColor;
            if (wantDemote)
            {
                if (!c.Demoted)
                {
                    c.Demoted = true;
                    VRLog.Info("WorldUI", $"FlatScreen stack: non-base '{cam.name}' (depth {cam.depth:F1}) " +
                                          "fullscreen SolidColor clear DEMOTED to Depth — its clear is a video " +
                                          "backdrop (VideoCamera.cs:96-97) and must not wipe the RT composite.");
                }
                if (cam.clearFlags == CameraClearFlags.SolidColor)
                    cam.clearFlags = CameraClearFlags.Depth;
            }
            else if (c.Demoted)
            {
                c.Demoted = false;
                if (cam.clearFlags == CameraClearFlags.Depth)
                    cam.clearFlags = c.OriginalClearFlags;
                cam.backgroundColor = c.OriginalBackground;
                VRLog.Info("WorldUI", $"FlatScreen stack: '{cam.name}' clear demotion lifted " +
                                      $"(restored {c.OriginalClearFlags}).");
            }
        }
    }

    /// <summary>Undo everything <see cref="CaptureStack"/> did and drop all references.</summary>
    private void ReleaseStack()
    {
        // Mirrors shadow captured cameras — they die with the stack and are rebuilt
        // by the next capture sweep (the stereo RT/hook lifecycle stays with Hide).
        _stereo.ReleaseMirrors();
        for (int i = 0; i < _captured.Count; i++)
        {
            Camera cam = _captured[i].Camera;
            if (cam == null)
                continue;
            if (cam.targetTexture == _rt || (_uiRt != null && cam.targetTexture == _uiRt))
                cam.targetTexture = null;
            // Only the stack bases (forced clears) and demoted overlays (SolidColor
            // → Depth) had their flags touched — leave everyone else alone (their
            // flags may have been legitimately changed by game code meanwhile).
            if (_captured[i] == _base || _captured[i] == _uiBase || _captured[i].Demoted)
            {
                cam.clearFlags = _captured[i].OriginalClearFlags;
                cam.backgroundColor = _captured[i].OriginalBackground;
            }
        }
        if (_captured.Count > 0)
            VRLog.Info("WorldUI", $"FlatScreen stack released — {_captured.Count} camera(s) restored to the backbuffer.");
        _captured.Clear();
        CapturedSet.Clear();
        _base = null;
        _uiBase = null;
        // The no-UI watchdog's evidence dies with the stack (scene change / hide):
        // the NEXT scene may well have a UI camera — give the split a fresh chance
        // (test #20: the latch carried from the UI-less loading scene into the main
        // menu and kept split/stereo/video-depth off for the whole session).
        if (_splitNoUi)
        {
            _splitNoUi = false;
            VRLog.Info("WorldUI", "Screen layer split no-UI fallback re-armed — captured stack " +
                                  "released (scene change / hide); the split re-engages on the next sweep.");
        }
    }

}

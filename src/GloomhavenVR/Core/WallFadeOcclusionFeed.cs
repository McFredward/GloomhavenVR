using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// ISSUE #4 (wall see-through) — feed the VR head camera the SCREEN-SPACE OCCLUSION MAP the
/// wall shaders actually fade by. This is the missing half of the [Compat] WallFade toggle.
///
/// THE REAL FADE MECHANISM (established by disassembling the wall shaders themselves —
/// DXBC fragment programs of <c>Amp_Basic_WallFade</c>, misc_high_shaders bundle, and
/// <c>Amp_Low/Amp_Basic_WallFade_Low</c>, misc_shaders bundle; tools/ShaderDisasm workflow):
/// the fade condition does NOT live in camera built-ins alone. Per fragment the shader
/// <list type="number">
/// <item>gates on the global int <c>ToggleWallFade</c> (the low variant: <c>ine cb0[4].x, 0</c>
///   — exactly the global that <c>Compat.WallFadeDisable</c> pins to the toggle),</item>
/// <item>samples <c>_TilesOcclusionMap</c> at its own SCREEN-SPACE UV,</item>
/// <item>compares the map's stored play-area depth (<c>occ.a</c>) against the wall fragment's
///   own clip depth (recomputed from <c>unity_MatrixVP</c>), and</item>
/// <item>discards (dissolves — <c>RenderType=TransparentCutout</c>) when the wall is NEARER
///   than the play area behind that pixel, i.e. exactly when the wall occludes the board
///   from the rendering camera. The high-quality variant additionally modulates with a
///   screen-center/camera-distance vignette and a noise dither, but the map term is the
///   trigger; the low variant uses the map term ONLY.</item>
/// </list>
/// <c>_TilesOcclusionMap</c> is an RT rendered EVERY FRAME by the game's
/// <c>TilesOcclusionGenerator</c> (decompiled GH.Runtime) via a <see cref="CommandBuffer"/>
/// on the GAME camera (<c>CameraEvent.BeforeGBuffer</c>): room/tile renderers → occlusion
/// material → pixel correction (needs the rendering camera's view matrices) → blur →
/// <c>SetGlobalTexture("_TilesOcclusionMap")</c> + <c>_EnableOcclusionMap=1</c>. Screen-space
/// UV + <c>unity_MatrixVP</c> depth mean the map is only valid for the exact viewpoint that
/// generated it.
///
/// WHY THE TOGGLE ALONE DID NOTHING ON HARDWARE: in VR the mod's head camera renders the
/// scene, but the generator's CB sits on the parked game camera — the head camera render
/// samples a map that is missing/stale/built for the WRONG viewpoint, so the fade term
/// collapses to "visible" and no wall ever fades, regardless of <c>ToggleWallFade</c>.
/// (An earlier revision of this feed existed as <c>Compat.TilesOcclusionMirror</c> and was
/// removed in af95ad0 "no visible effect" — correctly so at the time: back then
/// <c>ToggleWallFade</c> was pinned 0, so a valid map could not have had a visible effect
/// on walls. The toggle and the map need each other.)
///
/// THE FIX: replicate <c>TilesOcclusionGenerator.UpdateCommandBuffers()</c> 1:1 on the HEAD
/// camera — same RT formats/divisors/passes/global names, our own temp-RT ids and COPIES of
/// the four materials (the pixel-corrector needs OUR camera's view matrices; the game's
/// materials are never written) — attached at <c>BeforeGBuffer</c> (deferred) or
/// <c>BeforeForwardOpaque</c> (forward, the VR default). Globals set inside a CB apply at
/// execution time per camera render, so the game camera keeps its own map and ours sees the
/// head-camera map. MultiPass stereo: <see cref="Camera.onPreRender"/> fires per eye and
/// refreshes the pixel-corrector matrices with that eye's view matrix.
///
/// Gated LIVE by <c>[Compat] WallFade</c> (the same toggle WallFadeDisable consults): OFF
/// detaches the CB — zero cost, vanilla; ON attaches it so the game's own fade logic gets a
/// correct per-eye map and walls between the HMD and the play area dissolve exactly like the
/// flat game's walls do for its top-down camera. Purely visual and local; peers unaffected.
/// </summary>
internal static class WallFadeOcclusionFeed
{
    private const string Name = "WallFadeOcclusionFeed";
    private const string DriverName = "GloomhavenVR.WallFadeOcclusionFeed";

    private static FeedDriver? _driver;

    /// <summary>Install the feed driver (idempotent). No-op when VR isn't running.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        var go = new GameObject(DriverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<FeedDriver>();
        VRLog.Info(Name,
            $"installed (WallFade={(Plugin.WallFade != null && Plugin.WallFade.Value ? "on" : "off")}) — " +
            "will mirror the game's tiles-occlusion map (the wall shaders' fade input) onto the " +
            "VR head camera while the toggle is on.");
    }

    /// <summary>Detach the CB from whatever camera holds it and destroy the driver (hot-reload safe).</summary>
    public static void Uninstall()
    {
        if (_driver == null)
            return;
        try { _driver.Teardown("uninstall"); }
        catch { /* camera/scene already torn down */ }
        try { UnityEngine.Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    private static bool Enabled => Plugin.WallFade != null && Plugin.WallFade.Value;

    /// <summary>
    /// The per-frame driver. Activation gate: config ON, <c>TilesOcclusionGenerator.s_Instance</c>
    /// and <c>VRRigDriver.HeadCamera</c> alive. Rebuild triggers: head camera changed (rig
    /// rebuilds), scene load, rendering-path change, and a ~2s poll comparing the generator's
    /// renderer-list counts and the head camera's pixel dimensions against what the current CB
    /// was built with.
    /// </summary>
    private sealed class FeedDriver : MonoBehaviour
    {
        private const float PollIntervalSeconds = 2f;

        // Our own temp-RT ids (the sequence and formats replicate the game's exactly; distinct
        // names keep our per-camera temporaries from ever aliasing the game's).
        private static readonly int TempFullId = Shader.PropertyToID("_VRTilesTemp10");
        private static readonly int TempHalfAId = Shader.PropertyToID("_VRTilesTemp11");
        private static readonly int TempHalfBId = Shader.PropertyToID("_VRTilesTemp12");
        private static readonly int TempObjectsId = Shader.PropertyToID("_VRTilesObjectsRT");
        private static readonly int BlurObjAId = Shader.PropertyToID("_VRTilesBlurObjectsRT11");
        private static readonly int BlurObjBId = Shader.PropertyToID("_VRTilesBlurObjectsRT12");
        private static readonly int EnableOcclusionMapId = Shader.PropertyToID("_EnableOcclusionMap");
        private static readonly int OffsetScaleId = Shader.PropertyToID("tilesOcclusionOffsetScale");

        // Copied materials (ours; created from the generator's instances, destroyed on teardown).
        private Material? _occlusionMat;
        private Material? _occlusionObjectMat;
        private Material? _pixelCorrectorMat;
        private Material? _blurMat;

        /// <summary>Generator instance our material copies came from (recopy when it changes).</summary>
        private TilesOcclusionGenerator? _materialSource;

        private CommandBuffer? _cb;
        private Camera? _attachedCamera;
        private CameraEvent _attachedEvent;
        private RenderingPath _builtPath;
        private int _builtRoomCount = -1;
        private int _builtObjectCount = -1;
        private int _builtWidth;
        private int _builtHeight;

        private float _nextPoll;
        private bool _forceRebuild;
        private bool _heartbeatLogged;
        private bool _zoomFallbackLogged;
        private bool _failureLogged;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Camera.onPreRender += OnCameraPreRender;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Camera.onPreRender -= OnCameraPreRender;
        }

        private void OnDestroy() => Teardown("driver destroyed");

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Scenario scenes are additive and the generator's volumes stream in — always rebuild.
            _forceRebuild = true;
            _heartbeatLogged = false;
        }

        /// <summary>
        /// LateUpdate on purpose: it runs AFTER every game Update (guaranteed by Unity's player
        /// loop), i.e. after the generator consumed <c>m_RenderersUpdated</c> and rebuilt its own
        /// CB — our count-poll then sees the post-purge list state, and any zoom the game logic
        /// produced this frame is final before we derive <c>tilesOcclusionOffsetScale</c> from it.
        /// Fully guarded — a throw here must never starve the game loop (WorldUI lesson).
        /// </summary>
        private void LateUpdate()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    VRLog.Warn(Name, $"driver tick threw (logged once): {e}");
                }
            }
        }

        private void Tick()
        {
            if (!Enabled || !VRSession.IsRunning)
            {
                if (_attachedCamera != null)
                    Detach("WallFade off / VR stopped");
                return;
            }

            TilesOcclusionGenerator gen = TilesOcclusionGenerator.s_Instance;
            Camera? head = Rig.VRRigDriver.HeadCamera;
            if (gen == null || head == null)
            {
                if (_attachedCamera != null)
                    Detach(gen == null ? "generator gone" : "head camera gone");
                return;
            }

            // (Re)attach / rebuild policy.
            bool rebuild = _forceRebuild || _cb == null || _attachedCamera != head;
            float now = Time.unscaledTime;
            if (!rebuild && now >= _nextPoll)
            {
                _nextPoll = now + PollIntervalSeconds;
                rebuild = gen.m_RoomRenderers.Count != _builtRoomCount
                          || gen.m_ObjectRenderers.Count != _builtObjectCount
                          || head.pixelWidth != _builtWidth
                          || head.pixelHeight != _builtHeight
                          || head.actualRenderingPath != _builtPath;
            }
            if (rebuild)
            {
                _forceRebuild = false;
                _nextPoll = now + PollIntervalSeconds;
                Rebuild(gen, head);
                if (_cb == null)
                    return; // materials not ready yet — retry next poll
            }

            // Per-frame parameter drive (our copies only — the game's materials are never touched).
            // Mono baseline matrices; Camera.onPreRender refines them per eye right before each
            // eye's render (MultiPass fires it once per eye, after LateUpdate).
            if (_pixelCorrectorMat != null)
            {
                _pixelCorrectorMat.SetMatrix("_CameraInvViewMatrix", head.cameraToWorldMatrix);
                _pixelCorrectorMat.SetMatrix("_CameraViewMatrix", head.worldToCameraMatrix);
            }
            if (_occlusionMat != null)
            {
                // Same formula the generator uses each Update. The RTS zoom still ticks in VR
                // (the controller object survives; only its camera is retargeted offscreen).
                CameraController zoomSource = CameraController.s_CameraController;
                float offsetScale;
                if (zoomSource != null)
                {
                    offsetScale = (zoomSource.Zoom - 30f) / 45f;
                }
                else
                {
                    offsetScale = 0.5f;
                    if (!_zoomFallbackLogged)
                    {
                        _zoomFallbackLogged = true;
                        VRLog.Info(Name, "CameraController.s_CameraController unavailable — "
                            + "tilesOcclusionOffsetScale pinned to 0.5 (logged once).");
                    }
                }
                _occlusionMat.SetFloat(OffsetScaleId, offsetScale);
            }

            if (!_heartbeatLogged)
            {
                _heartbeatLogged = true;
                VRLog.Info(Name,
                    $"heartbeat scene='{SceneManager.GetActiveScene().name}': feeding "
                    + $"'{_attachedCamera!.name}' at {_attachedEvent} ({_builtPath}), "
                    + $"rooms={_builtRoomCount} objects={_builtObjectCount} "
                    + $"eyeDims={_builtWidth}x{_builtHeight} — wall shaders now sample a "
                    + "head-viewpoint _TilesOcclusionMap (their fade input) while "
                    + "WallFadeDisable holds ToggleWallFade=1.");
            }
        }

        /// <summary>
        /// Per-eye matrix refinement. MultiPass renders the head camera once per eye and
        /// <see cref="Camera.onPreRender"/> fires before each pass, on the main thread, before the
        /// camera's commands (including our CB) are submitted — a material write here is what that
        /// eye's pixel-corrector blits see.
        /// </summary>
        private void OnCameraPreRender(Camera cam)
        {
            if (cam == null || cam != _attachedCamera || _pixelCorrectorMat == null)
                return;
            try
            {
                Matrix4x4 view = cam.stereoActiveEye switch
                {
                    Camera.MonoOrStereoscopicEye.Left => cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left),
                    Camera.MonoOrStereoscopicEye.Right => cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right),
                    _ => cam.worldToCameraMatrix,
                };
                _pixelCorrectorMat.SetMatrix("_CameraViewMatrix", view);
                _pixelCorrectorMat.SetMatrix("_CameraInvViewMatrix", view.inverse);
            }
            catch
            {
                // Stereo matrices unavailable (headset mid-teardown) — mono baseline already set.
            }
        }

        /// <summary>Copy the generator's four materials (recopied when the generator instance changes).</summary>
        private bool EnsureMaterials(TilesOcclusionGenerator gen)
        {
            if (_materialSource == gen && _occlusionMat != null && _occlusionObjectMat != null
                && _pixelCorrectorMat != null && _blurMat != null)
                return true;

            DestroyMaterials();
            // Publicized private instance fields (GH.Runtime is compiled with Publicize=true).
            // Created in the generator's Awake — null only in a window we simply retry past.
            if (gen.m_OcclusionMaterial == null || gen.m_OcclusionObjectMaterial == null
                || gen.m_PixelCorrectorMaterial == null || gen.m_BlurMaterial == null)
                return false;

            _occlusionMat = new Material(gen.m_OcclusionMaterial);
            _occlusionObjectMat = new Material(gen.m_OcclusionObjectMaterial);
            _pixelCorrectorMat = new Material(gen.m_PixelCorrectorMaterial);
            _blurMat = new Material(gen.m_BlurMaterial);
            _materialSource = gen;
            return true;
        }

        /// <summary>
        /// Replicate <c>TilesOcclusionGenerator.UpdateCommandBuffers()</c> 1:1 for the head
        /// camera: same formats (R8G8B8A8_SNorm), same -1/-2/-4 camera-relative size divisors,
        /// same clear colors, same draw/blit/blur sequence and passes, same global names — only
        /// the camera, the materials (our copies), the temp-RT ids and the <c>offsets</c>
        /// denominators (head pixel dims instead of <c>Screen</c>) differ. The renderer LISTS are
        /// the generator's live lists, read (never mutated) at build time; null entries are
        /// skipped without purging (purging them is the game's own rebuild's job).
        /// </summary>
        private void Rebuild(TilesOcclusionGenerator gen, Camera head)
        {
            Detach(null); // silent — the attach line below reports the outcome

            if (!EnsureMaterials(gen))
            {
                VRLog.Debug(Name, "generator materials not ready yet — rebuild deferred to next poll.");
                return;
            }

            int width = Mathf.Max(head.pixelWidth, 1);
            int height = Mathf.Max(head.pixelHeight, 1);

            var cb = new CommandBuffer { name = "GloomhavenVR WallFade Occlusion Feed" };

            // ---- room (tiles) map: full-res draw with depth → pixel-correct → 2x blur --------
            cb.GetTemporaryRT(TempFullId, -1, -1, 24, FilterMode.Bilinear, GraphicsFormat.R8G8B8A8_SNorm);
            cb.GetTemporaryRT(TempHalfAId, -2, -2, 0, FilterMode.Bilinear, GraphicsFormat.R8G8B8A8_SNorm);
            cb.GetTemporaryRT(TempHalfBId, -2, -2, 0, FilterMode.Bilinear, GraphicsFormat.R8G8B8A8_SNorm);
            cb.SetRenderTarget(TempFullId);
            cb.ClearRenderTarget(clearDepth: true, clearColor: true, new Color(0f, 0f, 0f, 1f), 0f);
            int rooms = 0;
            foreach (MeshRenderer roomRenderer in gen.m_RoomRenderers)
            {
                if (roomRenderer == null)
                    continue;
                cb.DrawRenderer(roomRenderer, _occlusionMat, 0, 0);
                rooms++;
            }
            cb.Blit(TempFullId, TempHalfAId, _pixelCorrectorMat, 2, 0);
            cb.SetGlobalVector("offsets", new Vector4(4f / width, 0f, 0f, 0f));
            cb.Blit(TempHalfAId, TempHalfBId, _blurMat);
            cb.SetGlobalVector("offsets", new Vector4(0f, 4f / height, 0f, 0f));
            cb.Blit(TempHalfBId, TempHalfAId, _blurMat);

            // ---- object map: half-res draw with depth → downsample → 2x blur -----------------
            cb.GetTemporaryRT(TempObjectsId, -2, -2, 24, FilterMode.Bilinear, GraphicsFormat.R8G8B8A8_SNorm);
            cb.GetTemporaryRT(BlurObjAId, -4, -4, 0, FilterMode.Bilinear, GraphicsFormat.R8G8B8A8_SNorm);
            cb.GetTemporaryRT(BlurObjBId, -4, -4, 0, FilterMode.Bilinear, GraphicsFormat.R8G8B8A8_SNorm);
            cb.SetRenderTarget(TempObjectsId);
            cb.ClearRenderTarget(clearDepth: true, clearColor: true, Color.black, 0f);
            int objects = 0;
            foreach (MeshRenderer objectRenderer in gen.m_ObjectRenderers)
            {
                if (objectRenderer == null)
                    continue;
                cb.DrawRenderer(objectRenderer, _occlusionObjectMat);
                objects++;
            }
            cb.Blit(TempObjectsId, BlurObjAId);
            cb.SetGlobalVector("offsets", new Vector4(4f / width, 0f, 0f, 0f));
            cb.Blit(BlurObjAId, BlurObjBId, _blurMat);
            cb.SetGlobalVector("offsets", new Vector4(0f, 4f / height, 0f, 0f));
            cb.Blit(BlurObjBId, BlurObjAId, _blurMat);
            cb.SetGlobalTexture("_ObjectOcclusion", BlurObjAId);

            // ---- final tiles map + globals (execution-time, i.e. per this camera's render) ---
            cb.Blit(TempHalfAId, TempHalfBId, _pixelCorrectorMat, 3, 0);
            cb.SetGlobalTexture("_TilesOcclusionMap", TempHalfBId);
            cb.SetGlobalFloat(EnableOcclusionMapId, 1f);

            // Deferred (the game's own path) hooks BeforeGBuffer exactly like the original;
            // forward (the VR head camera's path) gets the equivalent pre-opaque slot.
            CameraEvent evt = head.actualRenderingPath == RenderingPath.DeferredShading
                ? CameraEvent.BeforeGBuffer
                : CameraEvent.BeforeForwardOpaque;
            head.AddCommandBuffer(evt, cb);

            _cb = cb;
            _attachedCamera = head;
            _attachedEvent = evt;
            _builtPath = head.actualRenderingPath;
            _builtRoomCount = gen.m_RoomRenderers.Count;
            _builtObjectCount = gen.m_ObjectRenderers.Count;
            _builtWidth = width;
            _builtHeight = height;

            VRLog.Info(Name,
                $"attached feed CB to '{head.name}' at {evt} (path {_builtPath}): "
                + $"rooms={rooms}/{_builtRoomCount} objects={objects}/{_builtObjectCount} "
                + $"eyeDims={width}x{height}.");
        }

        /// <summary>
        /// Remove our CB from the camera we attached it to (guarding against the camera dying
        /// first — rig rebuilds destroy the head camera GO) and dispose it.
        /// </summary>
        private void Detach(string? reason)
        {
            if (_cb != null)
            {
                if (_attachedCamera != null)
                {
                    try { _attachedCamera.RemoveCommandBuffer(_attachedEvent, _cb); }
                    catch { /* camera destroyed — its CB list died with it */ }
                }
                try { _cb.Dispose(); }
                catch { /* already disposed */ }
                _cb = null;
            }
            _attachedCamera = null;
            _builtRoomCount = -1;
            _builtObjectCount = -1;
            if (reason != null)
                VRLog.Info(Name, $"feed CB detached ({reason}).");
        }

        /// <summary>Full teardown: detach the CB and destroy our material copies.</summary>
        internal void Teardown(string reason)
        {
            Detach(reason);
            DestroyMaterials();
        }

        private void DestroyMaterials()
        {
            DestroyMaterial(ref _occlusionMat);
            DestroyMaterial(ref _occlusionObjectMat);
            DestroyMaterial(ref _pixelCorrectorMat);
            DestroyMaterial(ref _blurMat);
            _materialSource = null;
        }

        private static void DestroyMaterial(ref Material? mat)
        {
            if (mat != null)
            {
                try { Destroy(mat); }
                catch { /* already gone with the scene */ }
                mat = null;
            }
        }
    }
}

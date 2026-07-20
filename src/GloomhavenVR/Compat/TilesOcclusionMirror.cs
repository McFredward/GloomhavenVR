using System;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 endgame — mirror the game's OWN screen-space occlusion map onto the VR head camera.
///
/// MECHANISM (decompiled <c>TilesOcclusionGenerator</c>, GH.Runtime): the game's VFX shaders
/// (torch/candle flames <c>VFX/ParticleMasterUnlitAdd_Shd</c>, moths <c>VFX/WingFlap_Shd</c>, …)
/// hide themselves behind walls IN THE FRAGMENT SHADER by sampling a screen-space occlusion map
/// published through the globals <c>_TilesOcclusionMap</c>, <c>_ObjectOcclusion</c>,
/// <c>_EnableOcclusionMap</c> and <c>tilesOcclusionOffsetScale</c>. The generator (a MonoBehaviour
/// on the GAME camera) builds that map in a <see cref="CommandBuffer"/> at
/// <see cref="CameraEvent.BeforeGBuffer"/>: room renderers → occlusion material → temp RT (with
/// depth) → pixel correction (needs the rendering camera's view matrices) → two-pass blur →
/// object renderers → object map → globals. In VR the mod renders through its OWN head camera
/// (<see cref="Rig.VRRigDriver.HeadCamera"/>) while the game camera is retargeted offscreen — the
/// map is computed for the WRONG viewpoint, so the VFX self-hiding is garbage and flames/moths
/// shine through walls.
///
/// THE FIX: replicate the exact same CommandBuffer on the HEAD camera, driven by the head
/// camera's matrices and pixel dimensions, drawing the generator's LIVE renderer lists. Globals
/// set inside a CommandBuffer are applied AT EXECUTION TIME on the render thread, in submission
/// order within the frame: the game camera (lower depth) renders first and its CB points the
/// globals at its own map; our camera renders after and our CB re-points them at OUR map before
/// our opaque/VFX passes sample them. Each camera therefore sees its own correct map — the
/// game's native per-pixel occlusion, from the VR viewpoint, original look, zero toggling.
///
/// MATERIALS — COPIED, NOT SHARED (the deliberate choice): the pixel-corrector needs
/// <c>_CameraInvViewMatrix</c>/<c>_CameraViewMatrix</c> for the camera being rendered, and the
/// game writes ITS camera's matrices into ITS material every Update. Sharing that material and
/// overwriting the matrices in LateUpdate would deterministically win for the render phase (all
/// Updates complete before any LateUpdate) — but it would also corrupt the GAME camera's map
/// (both cameras render after the script phase, so the game camera would render with OUR
/// matrices), and it would couple us to script-order forever. Instead the driver instantiates
/// its own copies of the four materials (<c>new Material(theirs)</c> — same shaders, same
/// state) and never writes to game state at all: the game's generator keeps producing its own
/// (offscreen) map untouched, and no write race exists in either direction.
///
/// STEREO (MultiPass) caveat: <see cref="Camera.onPreRender"/> fires once per eye, and the
/// per-eye view matrix is written into our pixel-corrector copy right there
/// (<see cref="Camera.GetStereoViewMatrix"/>), so each eye's map is corrected with that eye's
/// actual matrix. The map is low-res and blurred, so any residual eye offset is invisible.
/// Cost: the map passes run once per eye (rooms+objects at occlusion-shader complexity plus
/// four half/quarter-res blur blits) — cheap relative to the scene render.
///
/// Config: <c>[Compat] TilesOcclusionMirror</c> (default true) in its own module file
/// (<c>dev.gloomhavenvr.occlusionmirror.cfg</c> — new file, so the BepInEx persisted-config
/// trap cannot resurrect a stale value). Read live: flipping it off detaches the CB and the
/// game's globals win again (vanilla behavior). VR-gated, reversible, never throws into the
/// game (first failure logged, then silent for that frame path).
/// </summary>
internal static class TilesOcclusionMirror
{
    private const string Name = "TilesOcclusionMirror";
    private const string DriverName = "GloomhavenVR.TilesOcclusionMirror";

    private static ConfigFile? _configFile;
    private static ConfigEntry<bool>? _enabled;
    private static MirrorDriver? _driver;

    /// <summary>Install the mirror driver (idempotent). No-op when VR isn't running.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        EnsureConfig();
        var go = new GameObject(DriverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<MirrorDriver>();
        VRLog.Info(Name,
            $"installed (config TilesOcclusionMirror={_enabled?.Value ?? true}) — will mirror the game's "
            + "tiles-occlusion CommandBuffer onto the VR head camera when both exist.");
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

    private static void EnsureConfig()
    {
        if (_enabled != null)
            return;
        _configFile = ModuleConfig.Create("occlusionmirror");
        _enabled = _configFile.Bind("Compat", "TilesOcclusionMirror", true,
            "Mirror the game's own screen-space tiles-occlusion map (the mechanism its torch/"
            + "candle/moth VFX shaders use to hide themselves behind walls) onto the VR head "
            + "camera, so those effects are occluded correctly from the VR viewpoint. Preserves "
            + "the original look exactly — nothing is toggled or re-shaded. Disable only to "
            + "compare against vanilla (flames become visible through walls again).");
    }

    internal static bool Enabled => _enabled?.Value ?? true;

    /// <summary>
    /// The per-frame driver. Activation gate: BOTH <c>TilesOcclusionGenerator.s_Instance</c> and
    /// <c>VRRigDriver.HeadCamera</c> alive. Rebuild triggers: head camera changed (rig rebuilds),
    /// scene load, rendering-path change, and a ~2s poll comparing the generator's renderer-list
    /// counts and the head camera's pixel dimensions against what the current CB was built with.
    /// </summary>
    private sealed class MirrorDriver : MonoBehaviour
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
                    Detach("config off / VR stopped");
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
                Camera gameCam = gen.GetComponent<Camera>();
                bool gameCamRendering = gameCam != null && gameCam.enabled;
                VRLog.Info(Name,
                    $"heartbeat scene='{SceneManager.GetActiveScene().name}': mirroring on "
                    + $"'{_attachedCamera!.name}' at {_attachedEvent} ({_builtPath}), "
                    + $"rooms={_builtRoomCount} objects={_builtObjectCount} "
                    + $"eyeDims={_builtWidth}x{_builtHeight}, "
                    + $"gameCameraStillRendering={gameCamRendering} (its own offscreen map stays "
                    + "intact — we copied the materials, and globals set inside a CommandBuffer "
                    + "apply per camera render in submission order, ours last).");
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

            var cb = new CommandBuffer { name = "GloomhavenVR Tile Occlusion Mirror" };

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
            // forward gets the equivalent pre-opaque slot.
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
                $"attached mirror CB to '{head.name}' at {evt} (path {_builtPath}): "
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
                VRLog.Info(Name, $"mirror CB detached ({reason}).");
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

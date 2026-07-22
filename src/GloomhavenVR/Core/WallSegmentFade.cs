using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// ISSUE #4 round 3 (wall see-through, VR redesign) — WHOLE-WALL fade with temporal hysteresis.
///
/// HARDWARE VERDICT on round 2 (per-pixel head-camera occlusion feed, removed with this class's
/// predecessor <c>WallFadeOcclusionFeed</c>): unusable in VR. The game's fade is a SCREEN-SPACE
/// per-pixel discard (wall fragment samples <c>_TilesOcclusionMap</c> at its own screen UV and
/// clips when it occludes the play area behind that pixel) — designed for a slow RTS camera.
/// Under fast head movement PARTS of walls pop in/out every frame. This driver replaces the
/// per-pixel screen-space decision with a per-WALL-SEGMENT decision computed on the CPU from the
/// head position, temporally smoothed, and delivered through the wall shaders' OWN fade path via
/// per-renderer <see cref="MaterialPropertyBlock"/>s.
///
/// WALL UNIT (research): scenario walls are <c>ProceduralWall</c> components (GH.Runtime,
/// Apparance procedural entities — one component per wall run with Left/RightCorner + Length;
/// all live instances in the publicized static <c>ProceduralWall.m_WallCache</c>). Their
/// generated child MeshRenderers carry the fade-capable shaders <c>Amp_Basic_WallFade</c>
/// (misc_high_shaders bundle) / <c>Amp_Low/Amp_Basic_WallFade_Low</c> (misc_shaders bundle).
/// The play area is the revealed room tiles: exactly <c>TilesOcclusionGenerator.s_Instance
/// .m_RoomRenderers</c> (fed per revealed room by <c>TilesOcclusionVolume.IsVisible()</c> ==
/// <c>m_HexMap.Revealed</c>) — the same renderer set the game itself rasterizes into the
/// occlusion map.
///
/// FADE MECHANISM (chosen: (a) the game's own map mechanism, forced per renderer — DXBC
/// disassembly of both wall fragment shaders, scratch <c>walldisasm[-low]</c>):
/// <list type="bullet">
/// <item>LOW variant: gate <c>ine cb0[4].x,0</c> (<c>ToggleWallFade</c>, int) AND object-space
///   Y &gt;= 0.4 (hard FOUNDATION BAND — the base course never fades); then
///   <c>factor = (occ.a &gt;= fragDepth) ? 1 : (1-occ.r)</c>, <c>discard if factor - _Cutoff &lt; 0</c>
///   (<c>_Cutoff</c> = the material's "Mask Clip Value", <c>cb0[4].y</c>).</item>
/// <item>HIGH variant: same map term, modulated by a world-pos 3D noise dither (x42) and
///   <c>visKeep = sat(3.33*((dist*0.02 + screenRadial)^8 + (1-worldY)/3))</c> — a SMOOTH
///   world-Y&lt;~1 foundation band + screen-edge vignette; final
///   <c>discard if (1 + ToggleWallFade*(a*b-1)) - _Cutoff &lt; 0</c> (<c>_Cutoff</c> = cb0[6].z).</item>
/// </list>
/// Unity property precedence is MPB &gt; material &gt; global, so a per-renderer
/// MaterialPropertyBlock can open the gate (<c>ToggleWallFade=1</c>), substitute the map
/// (<c>_TilesOcclusionMap</c> = a small CONSTANT texture with r=coverage, a=0 — alpha 0 fails
/// the reversed-Z depth test everywhere, i.e. "this wall occludes the play area at every
/// pixel"), and sweep <c>_Cutoff</c> to animate. Concretely:
/// <list type="bullet">
/// <item>TRANSITION (0&lt;fade&lt;1): map = low-frequency VALUE-NOISE texture (r in [0.06,1],
///   a=0), <c>_Cutoff = lerp(-0.05, 1, fade)</c>. Low variant discards where
///   <c>noise &gt; 1-_Cutoff</c> → progressive dissolve; high variant additionally dithers with
///   its own noise. View-independent inputs → no per-pixel popping from head motion (the
///   noise is sampled in screen space, so the pattern slides during the ~0.35s dissolve —
///   cosmetic only).</item>
/// <item>HELD FADED (fade=1): map = constant r=1,a=0 texture, <c>_Cutoff=0.5</c> —
///   deterministic full discard of everything the shader allows: the shader's OWN foundation
///   band survives ("bis auf die Grundmauern": object-Y&lt;0.4 hard on low, world-Y ramp
///   ~1.0→0.1 + peripheral vignette on high), matching the flat game's fully-faded look.</item>
/// <item>SOLID (fade=0): the MPB is REMOVED — with <see cref="Compat.WallFadeDisable"/> now
///   pinning the GLOBAL <c>ToggleWallFade</c> to 0 unconditionally (the game-camera
///   TilesOcclusionGenerator still publishes a head-viewpoint-invalid map; globally-open
///   fade would sample garbage), an untouched renderer is bit-for-bit today's solid wall.</item>
/// </list>
///
/// OCCLUSION DECISION (per segment, VR-stable): the head's gaze ray is intersected with the
/// room-renderer bounds to find the looked-at spot on the play area; that GAZE TARGET is
/// exponentially smoothed (tau 0.25s) so abrupt head rotation barely moves it. 7 sample points
/// (center + hex ring, radius 2.5) around the target; a segment's raw condition is "blocks the
/// head→sample segment for ≥2 of 7 samples" (Bounds.IntersectRay, hit before 88% of the sample
/// distance, or head inside the wall's AABB). The raw condition must persist 0.35s to switch a
/// segment ON and 0.55s to switch OFF (dwell hysteresis), and the fade value itself is
/// exponentially damped (tau 0.12s ≈ 0.35s visible transition). Head jitter → no change.
///
/// MULTIPLAYER: purely local rendering (MaterialPropertyBlocks + locally created textures);
/// nothing synced, peers unaffected. Gated LIVE by [Compat] WallFade — OFF clears every block
/// immediately (exactly today's solid walls, zero per-frame cost beyond the enabled check).
/// </summary>
internal static class WallSegmentFade
{
    private const string Name = "WallSegmentFade";
    private const string DriverName = "GloomhavenVR.WallSegmentFade";

    private static FadeDriver? _driver;

    /// <summary>Install the fade driver (idempotent). No-op when VR isn't running.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        var go = new GameObject(DriverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<FadeDriver>();
        VRLog.Info(Name,
            $"installed (WallFade={(Plugin.WallFade != null && Plugin.WallFade.Value ? "on" : "off")}) — " +
            "whole-wall fade: per-ProceduralWall occlusion decision from the head position, " +
            "dwell hysteresis + damped fade, delivered via per-renderer MaterialPropertyBlocks " +
            "through the wall shaders' own map/cutoff fade path.");
    }

    /// <summary>Clear every property block and destroy the driver (hot-reload safe).</summary>
    public static void Uninstall()
    {
        if (_driver == null)
            return;
        try { _driver.Teardown(); }
        catch { /* scene already tearing down */ }
        try { UnityEngine.Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    private static bool Enabled => Plugin.WallFade != null && Plugin.WallFade.Value;

    /// <summary>Per-wall-segment fade state.</summary>
    private sealed class Segment
    {
        public ProceduralWall? Wall;
        public readonly List<MeshRenderer> Renderers = new();
        public Bounds Bounds;
        public bool HasBounds;

        /// <summary>Last raw (unsmoothed) occlusion verdict and when it first held.</summary>
        public bool PendingRaw;
        public float PendingSince;
        /// <summary>Debounced state the fade animates toward.</summary>
        public bool State;
        /// <summary>Damped fade value in [0,1]; 0 = solid (no MPB), 1 = held fully faded.</summary>
        public float Fade;
        /// <summary>Whether our MPB is currently applied to the renderers.</summary>
        public bool HasBlock;
    }

    private sealed class FadeDriver : MonoBehaviour
    {
        // --- smoothing constants (see class header) -----------------------------------------
        private const float EnterDwellSeconds = 0.35f; // raw ON must persist this long
        private const float ExitDwellSeconds = 0.55f;  // raw OFF must persist this long
        private const float FadeTauSeconds = 0.12f;    // exp. fade time constant (~0.35s to 95%)
        private const float GazeTauSeconds = 0.25f;    // gaze-target damping
        private const float RingRadius = 2.5f;         // sample ring around the gaze target
        private const float SampleHeight = 0.4f;       // samples float this far above the target
        private const int BlockedNeeded = 2;           // of the 7 samples
        private const float RescanIntervalSeconds = 2f;

        private static readonly int TilesOcclusionMapId = Shader.PropertyToID("_TilesOcclusionMap");
        private static readonly int ToggleWallFadeId = Shader.PropertyToID("ToggleWallFade");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        private readonly Dictionary<ProceduralWall, Segment> _segments = new();
        private readonly List<ProceduralWall> _deadWalls = new();
        private readonly List<Bounds> _roomBounds = new();
        private readonly Vector3[] _samples = new Vector3[7];
        private readonly List<Material> _matScratch = new();
        private MaterialPropertyBlock? _mpb;

        private Texture2D? _noiseTex; // transition dissolve pattern (r in [0.06,1], a=0)
        private Texture2D? _fullTex;  // held-faded constant (r=1, a=0)

        private float _nextRescan;
        private int _builtRoomCount = -1;
        private Vector3 _gazeTarget;
        private bool _gazeInit;
        private bool _wasActive;
        private bool _failureLogged;
        private bool _heartbeatLogged;

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy() => Teardown();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Scenario scenes are additive; walls/volumes stream in — rescan promptly. Old
            // renderers die with their scene, so blocks need no explicit clearing here.
            _nextRescan = 0f;
            _builtRoomCount = -1;
            _heartbeatLogged = false;
        }

        /// <summary>
        /// LateUpdate on purpose: runs after every game Update, so the generator's renderer
        /// lists and the head pose are final for this frame. Fully guarded — a throw here must
        /// never starve the game loop (WorldUI lesson).
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
            Camera? head = Rig.VRRigDriver.HeadCamera;
            TilesOcclusionGenerator gen = TilesOcclusionGenerator.s_Instance;
            bool active = Enabled && VRSession.IsRunning && head != null && gen != null;
            if (!active)
            {
                // Toggled off / no scenario: revert to exactly-solid immediately.
                if (_wasActive)
                    ClearAllBlocks("inactive (toggle off / no scenario / no head)");
                _wasActive = false;
                return;
            }
            _wasActive = true;

            float now = Time.unscaledTime;
            if (now >= _nextRescan || gen!.m_RoomRenderers.Count != _builtRoomCount)
            {
                _nextRescan = now + RescanIntervalSeconds;
                Rescan(gen!);
            }
            if (_segments.Count == 0 || _roomBounds.Count == 0)
                return;

            Transform headT = head!.transform;
            Vector3 headPos = headT.position;
            UpdateGazeSamples(headPos, headT.forward);

            float fadeStep = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FadeTauSeconds);
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds)
                    continue;

                // Raw verdict + dwell hysteresis: the raw state must persist before the
                // debounced state follows it. Head jitter never survives the dwell.
                bool raw = IsBlocking(seg.Bounds, headPos);
                if (raw != seg.PendingRaw)
                {
                    seg.PendingRaw = raw;
                    seg.PendingSince = now;
                }
                if (seg.PendingRaw != seg.State
                    && now - seg.PendingSince >= (seg.PendingRaw ? EnterDwellSeconds : ExitDwellSeconds))
                {
                    seg.State = seg.PendingRaw;
                }

                // Critically-damped-style exponential fade toward the debounced state.
                float target = seg.State ? 1f : 0f;
                seg.Fade += (target - seg.Fade) * fadeStep;
                if (Mathf.Abs(target - seg.Fade) < 0.005f)
                    seg.Fade = target;

                Apply(seg);
            }

            if (!_heartbeatLogged)
            {
                _heartbeatLogged = true;
                VRLog.Info(Name,
                    $"heartbeat scene='{SceneManager.GetActiveScene().name}': tracking "
                    + $"{_segments.Count} wall segments against {_roomBounds.Count} room-renderer "
                    + "bounds — whole-wall fades now follow the head with dwell hysteresis "
                    + $"({EnterDwellSeconds:0.00}s in / {ExitDwellSeconds:0.00}s out, "
                    + $"tau {FadeTauSeconds:0.00}s).");
            }
        }

        // ---- occlusion decision -----------------------------------------------------------

        /// <summary>
        /// Gaze target = nearest hit of the head's forward ray on any room-renderer bounds
        /// (fallback: the point on the nearest room bounds closest to the ray), exponentially
        /// damped, then expanded into 1+6 sample points at foundation-top height.
        /// </summary>
        private void UpdateGazeSamples(Vector3 headPos, Vector3 headFwd)
        {
            var ray = new Ray(headPos, headFwd);
            float bestHit = float.MaxValue;
            foreach (Bounds b in _roomBounds)
            {
                if (b.IntersectRay(ray, out float d) && d >= 0f && d < bestHit)
                    bestHit = d;
            }

            Vector3 target;
            if (bestHit < float.MaxValue)
            {
                target = ray.GetPoint(bestHit);
            }
            else
            {
                // Not looking at the play area — aim at whichever room center best matches the
                // gaze direction, so a wall you face still counts when the board is behind it.
                float bestDot = float.MinValue;
                target = _gazeInit ? _gazeTarget : _roomBounds[0].center;
                foreach (Bounds b in _roomBounds)
                {
                    float dot = Vector3.Dot((b.center - headPos).normalized, headFwd);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        target = b.center;
                    }
                }
            }

            if (!_gazeInit)
            {
                _gazeInit = true;
                _gazeTarget = target;
            }
            else
            {
                float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / GazeTauSeconds);
                _gazeTarget += (target - _gazeTarget) * k;
            }

            _samples[0] = _gazeTarget + Vector3.up * SampleHeight;
            for (int i = 0; i < 6; i++)
            {
                float a = i * (Mathf.PI / 3f);
                _samples[i + 1] = _samples[0]
                    + new Vector3(Mathf.Cos(a) * RingRadius, 0f, Mathf.Sin(a) * RingRadius);
            }
        }

        /// <summary>
        /// True when the segment's AABB blocks head→sample for at least
        /// <see cref="BlockedNeeded"/> of the sample points (hit strictly between the head and
        /// ~88% of the way to the sample, so a wall AT the sample ring never self-triggers), or
        /// when the head is inside the AABB (wall in the face).
        /// </summary>
        private bool IsBlocking(Bounds b, Vector3 headPos)
        {
            if (b.Contains(headPos))
                return true;
            int blocked = 0;
            for (int i = 0; i < _samples.Length; i++)
            {
                Vector3 to = _samples[i] - headPos;
                float dist = to.magnitude;
                if (dist < 0.001f)
                    continue;
                var ray = new Ray(headPos, to / dist);
                if (b.IntersectRay(ray, out float d) && d < dist * 0.88f)
                {
                    if (++blocked >= BlockedNeeded)
                        return true;
                }
            }
            return false;
        }

        // ---- fade delivery ----------------------------------------------------------------

        /// <summary>
        /// Apply the segment's fade through the shader's own path (see class header).
        /// Reapplied every frame while faded because Apparance may regenerate wall renderers
        /// mid-fade; a null renderer triggers a prompt rescan.
        /// </summary>
        private void Apply(Segment seg)
        {
            if (seg.Fade <= 0f)
            {
                if (seg.HasBlock)
                {
                    seg.HasBlock = false;
                    foreach (MeshRenderer r in seg.Renderers)
                    {
                        if (r != null)
                            r.SetPropertyBlock(null);
                    }
                }
                return;
            }

            if (!EnsureTextures())
                return;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetInteger(ToggleWallFadeId, 1);
            if (seg.Fade >= 1f)
            {
                // Held fully faded: deterministic discard of everything above the shader's own
                // foundation band — the flat game's fully-faded wall appearance.
                _mpb.SetTexture(TilesOcclusionMapId, _fullTex!);
                _mpb.SetFloat(CutoffId, 0.5f);
            }
            else
            {
                // Dissolve: sweep the clip threshold across the noise texture's value range.
                _mpb.SetTexture(TilesOcclusionMapId, _noiseTex!);
                _mpb.SetFloat(CutoffId, Mathf.Lerp(-0.05f, 1f, seg.Fade));
            }

            seg.HasBlock = true;
            bool lostRenderer = false;
            foreach (MeshRenderer r in seg.Renderers)
            {
                if (r == null)
                {
                    lostRenderer = true;
                    continue;
                }
                r.SetPropertyBlock(_mpb);
            }
            if (lostRenderer)
                _nextRescan = 0f; // wall regenerated mid-fade — re-collect promptly
        }

        // ---- segment / play-area bookkeeping ------------------------------------------------

        /// <summary>
        /// Refresh the play-area bounds (from the generator's live room-renderer list) and the
        /// wall-segment table (from ProceduralWall.m_WallCache, publicized static). Renderers
        /// are matched by shader name ("WallFade") so props/doors under the same entity are
        /// never touched.
        /// </summary>
        private void Rescan(TilesOcclusionGenerator gen)
        {
            _roomBounds.Clear();
            foreach (MeshRenderer r in gen.m_RoomRenderers)
            {
                if (r != null)
                    _roomBounds.Add(r.bounds);
            }
            _builtRoomCount = gen.m_RoomRenderers.Count;

            // Drop segments whose wall died (their renderers died with them).
            _deadWalls.Clear();
            foreach (KeyValuePair<ProceduralWall, Segment> kv in _segments)
            {
                if (kv.Key == null)
                    _deadWalls.Add(kv.Key!); // destroyed Unity object — reference still hashes
            }
            foreach (ProceduralWall dead in _deadWalls)
                _segments.Remove(dead);

            // Adopt new walls / refresh renderer lists and bounds.
            List<ProceduralWall> cache = ProceduralWall.m_WallCache;
            for (int i = 0; i < cache.Count; i++)
            {
                ProceduralWall wall = cache[i];
                if (wall == null)
                    continue;
                if (!_segments.TryGetValue(wall, out Segment? seg))
                {
                    seg = new Segment { Wall = wall };
                    _segments.Add(wall, seg);
                }
                RefreshSegment(seg);
            }
        }

        /// <summary>Re-collect a segment's fade-capable renderers and combined AABB.</summary>
        private void RefreshSegment(Segment seg)
        {
            seg.Renderers.Clear();
            seg.HasBounds = false;
            if (seg.Wall == null)
                return;
            MeshRenderer[] all = seg.Wall.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            foreach (MeshRenderer r in all)
            {
                if (r == null || !HasWallFadeMaterial(r))
                    continue;
                seg.Renderers.Add(r);
                if (!seg.HasBounds)
                {
                    seg.Bounds = r.bounds;
                    seg.HasBounds = true;
                }
                else
                {
                    seg.Bounds.Encapsulate(r.bounds);
                }
                // Renderer may be brand new (Apparance rebuild) while the segment is mid-fade —
                // Apply() runs every frame for faded segments and will cover it.
            }
        }

        /// <summary>Does any shared material use one of the wall-fade shaders?</summary>
        private bool HasWallFadeMaterial(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m != null && m.shader != null && m.shader.name.Contains("WallFade"))
                    return true;
            }
            return false;
        }

        // ---- textures / teardown ------------------------------------------------------------

        /// <summary>
        /// Create the two delivery textures. Noise: 64x64 value noise (Mathf.PerlinNoise),
        /// rank-flattened to a uniform histogram over [0.06,1] so the _Cutoff sweep dissolves at
        /// a constant area-rate; low frequency keeps the left/right-eye patterns correlated
        /// (screen-space sampling differs per eye only by disparity). Alpha stays 0 in both
        /// textures = "play area behind every pixel" under the shader's reversed-Z compare.
        /// </summary>
        private bool EnsureTextures()
        {
            if (_noiseTex != null && _fullTex != null)
                return true;

            const int size = 64;
            _noiseTex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "GloomhavenVR.WallFadeNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            int n = size * size;
            var values = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                int x = i % size, y = i / size;
                // ~5 noise cells across the texture (which spans the SCREEN when sampled).
                values[i] = Mathf.PerlinNoise(x * (5f / size) + 11.31f, y * (5f / size) + 47.77f);
                order[i] = i;
            }
            Array.Sort(order, (a, b) => values[a].CompareTo(values[b]));
            var pixels = new Color32[n];
            for (int rank = 0; rank < n; rank++)
            {
                byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(0.06f, 1f, (rank + 0.5f) / n) * 255f);
                pixels[order[rank]] = new Color32(r, 0, 0, 0);
            }
            _noiseTex.SetPixels32(pixels);
            _noiseTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _fullTex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "GloomhavenVR.WallFadeFull",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var full = new Color32[4];
            for (int i = 0; i < 4; i++)
                full[i] = new Color32(255, 0, 0, 0);
            _fullTex.SetPixels32(full);
            _fullTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return true;
        }

        private void ClearAllBlocks(string reason)
        {
            int cleared = 0;
            foreach (Segment seg in _segments.Values)
            {
                seg.State = false;
                seg.PendingRaw = false;
                seg.Fade = 0f;
                if (!seg.HasBlock)
                    continue;
                seg.HasBlock = false;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null)
                    {
                        r.SetPropertyBlock(null);
                        cleared++;
                    }
                }
            }
            if (cleared > 0)
                VRLog.Info(Name, $"cleared property blocks on {cleared} renderers ({reason}).");
        }

        /// <summary>Full teardown: revert every renderer and destroy our textures.</summary>
        internal void Teardown()
        {
            try { ClearAllBlocks("teardown"); }
            catch { /* renderers already dying with the scene */ }
            _segments.Clear();
            _roomBounds.Clear();
            if (_noiseTex != null)
            {
                try { Destroy(_noiseTex); } catch { /* already gone */ }
                _noiseTex = null;
            }
            if (_fullTex != null)
            {
                try { Destroy(_fullTex); } catch { /* already gone */ }
                _fullTex = null;
            }
        }
    }
}

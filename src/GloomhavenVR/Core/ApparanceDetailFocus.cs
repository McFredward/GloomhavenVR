using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// ROOT FIX for the missing revealed-room geometry (fehlender_boden2/3.png): the game's
/// procedural map content is synthesized by the Apparance engine AROUND A VIEWPOINT, and
/// in VR that viewpoint is a PARKED camera — so mid-scenario room reveals regenerate into
/// a viewpoint that never looks at them and the room's floor/walls never materialize.
///
/// The full chain (all decompiled, 2026-08-02):
/// <list type="number">
/// <item><c>ApparanceEngine.UpdateEngine()</c> (Apparance.Unity) feeds the native
///   synthesis engine a view position every frame: <c>Engine.Update(0.1f, view_position)</c>
///   with <c>view_position = Camera.main.transform.position</c> — the tooltip on its own
///   <c>EnableDetailFocus</c> override names the semantics: "the centre of detail
///   generation (instead of the main camera)".</item>
/// <item>In a scenario <c>Camera.main</c> is 'ScenarioCamera' (tag MainCamera), which the
///   rig deliberately PARKS while VR runs (<c>CameraController.LateUpdate</c> /
///   <c>RefreshFocusPosition</c> prefix-skips, Rig/CameraControllerPatches.cs) — the
///   synthesis viewpoint is frozen at wherever scenario load left it, forever.</item>
/// <item>Hidden rooms are not merely deactivated content: <c>ApparanceEntity
///   .CheckEntity()</c> (Apparance.Unity) DESTROYS the native entity whenever its
///   GameObject is inactive in hierarchy (<c>else if (m_EntityHandle != 0)
///   DestroyEntity()</c>) and re-creates + refreshes it when the reveal re-activates the
///   subtree (<c>ProceduralMapTile.ApplyVisibility</c> → <c>ShowContent</c> SetActive).
///   So a door-open reveal *re-synthesizes the whole room from scratch* — against the
///   parked viewpoint. The flat game never shows this because its camera pans onto every
///   door/room it reveals (SmartFocus), dragging synthesis detail with it.</item>
/// </list>
///
/// ROUND 3 (fehlender_boden3.png): pointing the focus at the raw HEAD position made the
/// revealed tile BUILD (hardware log: maptile 'L' went Preview/0-renderers → All/229
/// renderers/handle=built) but the room still rendered near-empty — the full floor mesh
/// and every wall renderer were NEVER GENERATED (floor census: the only floor-band
/// renderer over the revealed room is an 'EN_Unseen_…' hex under the inactive 'Preview'
/// child; the ProceduralWall cache entries carry no renderer at all). The synthesis DID
/// run — but at the head's distance. The native engine receives a bare position
/// (<c>Engine.Update(0.1f, view_position)</c>, no direction, decompiled Apparance.Net)
/// and scales generated detail by DISTANCE from that point: tile 'B' synthesized at load
/// with the head ~10 wu away came out complete, tile 'L' re-synthesized at reveal with
/// the head ~29 wu away came out as coarse preview-grade scraps (inferred: the native
/// side is a black box, but it is the only input that differs between the two tiles).
/// The flat camera never sits that far out — SmartFocus pans it right onto every
/// revealed door.
///
/// FIX (round 3): feed the engine the BOARD POINT the player is looking at instead of
/// the head itself — the gaze ray's intersection with the board plane (y ≈ tile level),
/// clamped to the map's bounds and critically damped so synthesis never chases per-frame
/// head jitter. And because a door can be opened while looking elsewhere, any tile whose
/// Apparance entity is actively BUILDING (<c>ApparanceEntity.IsBusy</c>, cleared by the
/// native end-of-content-update task) takes priority: the focus parks on the building
/// tile until it finishes — exactly the flat game's SmartFocus behaviour, expressed as
/// a viewpoint. When no board exists (menus, world map) the focus falls back to the raw
/// head position — the proven round-2 behaviour.
///
/// REVERSIBLE: the engine's original EnableDetailFocus/DetailFocus are captured per
/// engine instance and restored on VR-off / hot reload; the focus object is destroyed.
/// MP-SAFE: the viewpoint only steers LOCAL synthesis scheduling/detail — map layout,
/// tiles, actors and all rules state are computed elsewhere (SRL) and synced by the
/// game; peers run their own engines against their own cameras. No net traffic, no
/// game-state mutation. TickGuard-safe: the driver's Update is fully guarded and a
/// throw can never starve the game loop.
/// </summary>
internal static class ApparanceDetailFocus
{
    private const string Name = "ApparanceDetailFocus";
    private const string DriverName = "GloomhavenVR.ApparanceDetailFocus";

    private static FocusDriver? _driver;

    /// <summary>Install the focus driver (idempotent). No-op when VR isn't running.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        var go = new GameObject(DriverName);
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<FocusDriver>();
        VRLog.Info(Name,
            "installed — Apparance synthesis viewpoint follows the player's GAZE POINT on " +
            "the board (head fallback off-board), and parks on any map tile whose entity " +
            "is building — reveal-time re-synthesis now happens at board distance, like " +
            "the flat camera's SmartFocus.");
    }

    /// <summary>Restore the engine's own viewpoint source and drop the driver.</summary>
    public static void Uninstall()
    {
        if (_driver == null)
            return;
        try { _driver.Restore("uninstall"); }
        catch { /* engine already tearing down */ }
        try { Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    private sealed class FocusDriver : MonoBehaviour
    {
        /// <summary>Seconds between map-tile cache rescans (tiles churn only on scene
        /// load/reveal; a busy tile is caught within half a second of build start while a
        /// per-frame FindObjectsOfType stays off the hot path).</summary>
        private const float TileScanInterval = 0.5f;

        /// <summary>SmoothDamp time for the focus point — long enough that saccades and
        /// head jitter never thrash the native detail centre mid-synthesis, short enough
        /// that the focus arrives on a revealed room well inside its build time.</summary>
        private const float FocusSmoothTime = 0.25f;

        /// <summary>Gaze rays are accepted up to this length — beyond it the player is
        /// looking at the horizon/sky and the last good board point is kept instead.</summary>
        private const float MaxGazeDistance = 200f;

        /// <summary>XZ margin added around the union of tile bounds when clamping the
        /// gaze point — lets the focus sit on a room edge without leaving the map.</summary>
        private const float BoardMargin = 6f;

        /// <summary>The transform the engine is pointed at; steered every frame.</summary>
        private GameObject? _focusGo;

        /// <summary>Engine instance we applied the override to (engines are per-app-lifetime,
        /// but scene churn / hot reload can hand us a fresh one — re-capture per instance).</summary>
        private ApparanceEngine? _appliedEngine;
        private bool _origEnable;
        private GameObject? _origFocus;
        private bool _failureLogged;

        // ---- focus-target state ----------------------------------------------------------
        private ProceduralMapTile[] _tiles = System.Array.Empty<ProceduralMapTile>();
        private ApparanceEntity?[] _tileEntities = System.Array.Empty<ApparanceEntity?>();
        private float _nextTileScan;
        private Bounds _boardBounds;
        private bool _hasBoardBounds;
        private ProceduralMapTile? _busyTile;
        private Vector3 _lastGazePoint;
        private bool _hasGazePoint;
        private Vector3 _focusVelocity;
        private bool _snapNext = true;

        private void OnDestroy()
        {
            try { Restore("driver destroyed"); }
            catch { /* engine already gone */ }
        }

        private void Update()
        {
            try { Tick(); }
            catch (System.Exception e)
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
            if (!VRSession.IsRunning || head == null)
            {
                // VR dropped mid-session: give the engine its own camera back immediately —
                // a dangling focus object would freeze synthesis at the last head position,
                // which is exactly the failure mode this driver exists to remove.
                Restore("VR not running / no head");
                return;
            }

            ApparanceEngine? engine = ApparanceEngine.Instance;
            if (engine == null)
            {
                // No engine (menus before first scenario) — nothing to steer. Keep any old
                // application restored so a destroyed engine never holds our object.
                Restore("no engine instance");
                return;
            }

            if (_focusGo == null)
            {
                _focusGo = new GameObject(DriverName + ".Focus");
                DontDestroyOnLoad(_focusGo);
                _snapNext = true;
            }

            RefreshTileCacheIfDue();
            Vector3 desired = ComputeDesiredFocus(head);

            // Critically damped follow — the native engine samples this point every frame
            // while streaming content in; a smoothed target keeps one reveal's synthesis
            // centred instead of chasing every glance.
            Transform t = _focusGo.transform;
            if (_snapNext)
            {
                _snapNext = false;
                _focusVelocity = Vector3.zero;
                t.position = desired;
            }
            else
            {
                t.position = Vector3.SmoothDamp(
                    t.position, desired, ref _focusVelocity, FocusSmoothTime);
            }

            if (_appliedEngine != engine || !engine.EnableDetailFocus
                || engine.DetailFocus != _focusGo)
            {
                if (_appliedEngine != engine)
                {
                    // Fresh engine instance: capture ITS authored values (not a stale pair).
                    _origEnable = engine.EnableDetailFocus;
                    _origFocus = engine.DetailFocus;
                }
                engine.EnableDetailFocus = true;
                engine.DetailFocus = _focusGo;
                _appliedEngine = engine;
                Camera? main = Camera.main;
                VRLog.Info(Name,
                    $"engine viewpoint OVERRIDDEN → gaze/board focus at {t.position} " +
                    $"(was Camera.main '{(main != null ? main.name : "<none>")}'" +
                    $"{(main != null ? $" parked at {main.transform.position}" : string.Empty)}; " +
                    $"authored EnableDetailFocus={_origEnable}) — revealed rooms now " +
                    "re-synthesize against a board-level viewpoint.");
            }
        }

        /// <summary>
        /// Pick the point the native engine should generate detail around, in priority
        /// order: (1) a map tile whose entity is BUILDING right now — a reveal must
        /// synthesize at board distance no matter where the player looks; (2) the gaze
        /// ray's intersection with the board plane, clamped to the map bounds; (3) the
        /// raw head position (menus / world map / no board — round-2 proven behaviour).
        /// </summary>
        private Vector3 ComputeDesiredFocus(Camera head)
        {
            Vector3 headPos = head.transform.position;

            if (TryGetBusyTileFocus(headPos, out Vector3 busyFocus))
                return busyFocus;

            if (TryGetGazeBoardFocus(head, out Vector3 gazeFocus))
                return gazeFocus;

            return headPos;
        }

        /// <summary>Rescan the scene's active map tiles on a slow cadence and cache the
        /// board's union bounds (tile BoxColliders, tile positions as fallback).</summary>
        private void RefreshTileCacheIfDue()
        {
            if (Time.unscaledTime < _nextTileScan)
                return;
            _nextTileScan = Time.unscaledTime + TileScanInterval;

            _tiles = FindObjectsOfType<ProceduralMapTile>(); // active only — hidden tiles have no entity
            if (_tileEntities.Length != _tiles.Length)
                _tileEntities = new ApparanceEntity?[_tiles.Length];
            _hasBoardBounds = false;
            for (int i = 0; i < _tiles.Length; i++)
            {
                ProceduralMapTile tile = _tiles[i];
                _tileEntities[i] = tile != null ? tile.GetComponent<ApparanceEntity>() : null;
                if (tile == null)
                    continue;
                BoxCollider? box = tile.BoxCollider;
                Bounds b = box != null
                    ? box.bounds
                    : new Bounds(tile.transform.position, new Vector3(20f, 4f, 20f));
                if (!_hasBoardBounds)
                {
                    _boardBounds = b;
                    _hasBoardBounds = true;
                }
                else
                {
                    _boardBounds.Encapsulate(b);
                }
            }
            if (!_hasBoardBounds)
                _hasGazePoint = false; // stale board gone (scene change) — drop the old point
        }

        /// <summary>
        /// A tile whose Apparance entity is mid-build owns the focus (sticky until the
        /// native end-of-content-update clears <c>IsBusy</c>). Reveal-time builds thereby
        /// always synthesize against a viewpoint ON the new room.
        /// </summary>
        private bool TryGetBusyTileFocus(Vector3 headPos, out Vector3 focus)
        {
            // Sticky: keep the tile we latched until it finishes — mid-build focus hops
            // would re-tier half the room.
            if (_busyTile != null && _busyTile.gameObject.activeInHierarchy
                && IsTileBusy(_busyTile))
            {
                focus = _busyTile.transform.position;
                return true;
            }
            if (_busyTile != null)
            {
                VRLog.Info(Name,
                    $"building tile '{_busyTile.name}' finished — focus returns to the gaze point.");
                _busyTile = null;
            }

            float bestSqr = float.MaxValue;
            ProceduralMapTile? best = null;
            for (int i = 0; i < _tiles.Length; i++)
            {
                ProceduralMapTile tile = _tiles[i];
                ApparanceEntity? entity = i < _tileEntities.Length ? _tileEntities[i] : null;
                if (tile == null || entity == null || !tile.gameObject.activeInHierarchy)
                    continue;
                bool busy;
                try { busy = entity.IsBusy; }
                catch { continue; } // engine tearing down mid-frame
                if (!busy)
                    continue;
                float d = (tile.transform.position - headPos).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = tile;
                }
            }
            if (best != null)
            {
                _busyTile = best;
                VRLog.Info(Name,
                    $"map tile '{best.name}' is building — synthesis focus parked on it at " +
                    $"{best.transform.position} (head at {headPos}).");
                focus = best.transform.position;
                return true;
            }
            focus = default;
            return false;
        }

        private bool IsTileBusy(ProceduralMapTile tile)
        {
            try
            {
                ApparanceEntity? entity = tile.GetComponent<ApparanceEntity>();
                return entity != null && entity.IsBusy;
            }
            catch { return false; }
        }

        /// <summary>
        /// The board point the player is LOOKING AT: gaze ray ∩ board plane (the tile
        /// bounds' base level), clamped into the map bounds. Looking at the sky/hands
        /// keeps the last good point instead of yanking the focus around.
        /// </summary>
        private bool TryGetGazeBoardFocus(Camera head, out Vector3 focus)
        {
            if (!_hasBoardBounds)
            {
                focus = default;
                return false;
            }

            float boardY = _boardBounds.min.y;
            Vector3 origin = head.transform.position;
            Vector3 dir = head.transform.forward;
            float denom = dir.y;
            // Require an actual crossing towards the plane (from either side) — a ray
            // parallel to the board never lands on it.
            if (Mathf.Abs(denom) > 1e-4f)
            {
                float t = (boardY - origin.y) / denom;
                if (t > 0f && t <= MaxGazeDistance)
                {
                    Vector3 hit = origin + dir * t;
                    hit.x = Mathf.Clamp(hit.x,
                        _boardBounds.min.x - BoardMargin, _boardBounds.max.x + BoardMargin);
                    hit.z = Mathf.Clamp(hit.z,
                        _boardBounds.min.z - BoardMargin, _boardBounds.max.z + BoardMargin);
                    hit.y = boardY;
                    _lastGazePoint = hit;
                    _hasGazePoint = true;
                }
            }
            focus = _lastGazePoint;
            return _hasGazePoint;
        }

        /// <summary>Put the engine back on its authored viewpoint source (idempotent).</summary>
        internal void Restore(string reason)
        {
            if (_appliedEngine != null)
            {
                // Only restore what is still ours — a game system that re-pointed DetailFocus
                // after us owns the field now.
                if (_appliedEngine.DetailFocus == _focusGo)
                {
                    _appliedEngine.EnableDetailFocus = _origEnable;
                    _appliedEngine.DetailFocus = _origFocus;
                    VRLog.Info(Name, $"engine viewpoint restored to authored source ({reason}).");
                    TeardownReport.Note("Apparance synthesis viewpoint (engine back on its authored source)");
                }
                _appliedEngine = null;
                _origFocus = null;
            }
            if (_focusGo != null)
            {
                try { Destroy(_focusGo); }
                catch { /* teardown */ }
                _focusGo = null;
            }
            _busyTile = null;
            _snapNext = true;
            _hasGazePoint = false;
        }
    }
}

using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// ROOT FIX for the missing revealed-room geometry (fehlender_boden2.png): the game's
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
/// Hardware-log proof (.planning/debug/Player.log): the reveal STATE chain all ran
/// ('Called Show Maptile: True', occlusion volumes 2→4 appended, enemies spawned) yet the
/// FLOOR CENSUS found no floor renderer over the revealed room's center and 3 of its
/// ProceduralWall cache entities carried renderers with no real wall materials — the
/// classic "requested but never synthesized" signature. The TilesOcclusionGenerator
/// hypothesis was REFUTED: it only rasterizes proxy footprints into the screen-space
/// _TilesOcclusionMap for the wall-fade shaders; it never toggles a renderer.
///
/// FIX — the engine's OWN seam, no Harmony: set <c>ApparanceEngine.EnableDetailFocus =
/// true</c> and point <c>DetailFocus</c> at a mod-owned GameObject that tracks the VR
/// HEAD's world position every frame. UpdateEngine's first branch then feeds the head as
/// the detail centre and the parked camera drops out of the equation entirely. The head
/// hovers 10-25 wu from any board point (nearer than the flat camera's 30-75 wu zoom
/// band), so everything the player can look at synthesizes at full detail.
///
/// REVERSIBLE: the engine's original EnableDetailFocus/DetailFocus are captured per
/// engine instance and restored on VR-off / hot reload; the focus object is destroyed.
/// MP-SAFE: the viewpoint only steers LOCAL synthesis scheduling/detail — map layout,
/// tiles, actors and all rules state are computed elsewhere (SRL) and synced by the
/// game; peers run their own engines against their own cameras. TickGuard-safe: the
/// driver's Update is fully guarded and a throw can never starve the game loop.
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
            "installed — Apparance synthesis viewpoint will follow the VR HEAD instead of " +
            "the parked Camera.main (EnableDetailFocus override; reveal-time room " +
            "re-synthesis needs a viewpoint that actually looks at the board).");
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
        /// <summary>The transform the engine is pointed at; repositioned to the head every frame.</summary>
        private GameObject? _focusGo;

        /// <summary>Engine instance we applied the override to (engines are per-app-lifetime,
        /// but scene churn / hot reload can hand us a fresh one — re-capture per instance).</summary>
        private ApparanceEngine? _appliedEngine;
        private bool _origEnable;
        private GameObject? _origFocus;
        private bool _failureLogged;

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
            }
            // World-space head position — UpdateEngine subtracts the engine transform itself.
            _focusGo.transform.position = head.transform.position;

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
                    $"engine viewpoint OVERRIDDEN → VR head at {_focusGo.transform.position} " +
                    $"(was Camera.main '{(main != null ? main.name : "<none>")}'" +
                    $"{(main != null ? $" parked at {main.transform.position}" : string.Empty)}; " +
                    $"authored EnableDetailFocus={_origEnable}) — revealed rooms now " +
                    "re-synthesize against the head position.");
            }
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
        }
    }
}

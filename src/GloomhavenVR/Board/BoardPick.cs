using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// The single per-frame VR board pick (Phase 3a). Computed lazily once per frame
/// (frame-memoized, no per-frame allocations) and consumed by:
///
/// - <see cref="Patches.MF_FindInteractableAtMousePosition_Patch"/> — replaces the
///   game's mouse ray with the VR pick ray,
/// - <see cref="Patches.InputManager_CursorPosition_Patch"/> — projects the pick
///   point into the game camera's screen space for all cursor consumers
///   (HoverRegisterer, melee-AoE facing, tooltips),
/// - <see cref="BoardClickDriver"/> — click commit,
/// - <see cref="TargetingUx"/> — hover haptics.
///
/// Two pick sources, arbitrated near-over-far:
/// - **Near**: an index fingertip within <c>[Board] TouchRange</c> above the board —
///   a short downward raycast from the fingertip (either hand; closest surface wins).
/// - **Far**: the primary hand's <see cref="RayInteractor"/> pick
///   (<see cref="VRHands.PrimaryPick"/> per INTERFACES-P2 §2) — including the
///   [Dev] SimulateHands fallback, which drives the same RayInteractor.
///
/// Inactive (all queries return false, patches run the ORIGINAL game code) while:
/// no scenario / modal UI (<see cref="VRModeStateMachine"/> Menu2D/ModalUI), the
/// scenario <c>Controller</c> is missing, or no hand can provide a pick (e.g. the
/// mode policy disabled both Ray and Poke) — so the mouse and the Phase-2 virtual
/// mouse keep working whenever VR is not actively picking.
/// </summary>
internal static class BoardPick
{
    internal enum PickSource
    {
        None,
        Far,
        Near
    }

    /// <summary>Raise the near-pick ray origin above the fingertip (real meters) so a finger already touching the surface still hits it.</summary>
    private const float NearOriginLift = 0.03f;

    /// <summary>Far-pick raycast length — same 1000f the game uses in MF.FindInteractableAtMousePosition.</summary>
    private const float FarMaxDistance = 1000f;

    /// <summary>Returned as the game cursor when the VR pick misses: far off-screen, so screen-ray consumers (HoverRegisterer) miss too.</summary>
    private static readonly Vector2 OffscreenCursor = new(-4096f, -4096f);

    private static int _frame = -1;
    private static PickSource _source;
    private static bool _inScenario;
    private static VRHand? _hand;
    private static Vector3 _rayOrigin;
    private static Vector3 _rayDirection;
    private static float _rayMaxDistance;
    private static bool _hasHit;
    private static Vector3 _hitPoint;
    private static Collider? _hitCollider;
    private static float _nearSurfaceDistance;
    private static Vector3 _cursorWorld;

    // ---- queries (all frame-memoized) --------------------------------------------------

    public static PickSource Source
    {
        get
        {
            EnsureFresh();
            return _source;
        }
    }

    public static bool Active => Source != PickSource.None;

    /// <summary>
    /// True while the mod is in a LIVE scenario (VR laser context): not Menu2D/ModalUI and a
    /// scenario <see cref="Controller"/> exists — regardless of whether a hand actually produced a
    /// pick this frame. Distinguishes "in a scenario but the ray gave no pick" (source==None yet
    /// InScenario) from "outside a scenario / behind a modal" (both false), which <see cref="Active"/>
    /// alone cannot. Consumed by <see cref="Patches.HexHoverClear"/> to kill the stale cursor-hover
    /// star even when no VR pick is produced.
    /// </summary>
    public static bool InScenario
    {
        get
        {
            EnsureFresh();
            return _inScenario;
        }
    }

    /// <summary>The hand providing the current pick (null while inactive).</summary>
    public static VRHand? SourceHand
    {
        get
        {
            EnsureFresh();
            return _hand;
        }
    }

    public static bool HasHit
    {
        get
        {
            EnsureFresh();
            return _hasHit;
        }
    }

    public static Collider? HitCollider
    {
        get
        {
            EnsureFresh();
            return _hitCollider;
        }
    }

    /// <summary>Near mode: signed fingertip-to-surface distance (world units; negative = pressed in).</summary>
    public static float NearSurfaceDistance
    {
        get
        {
            EnsureFresh();
            return _nearSurfaceDistance;
        }
    }

    /// <summary>
    /// The ray the game should raycast instead of its mouse ray. False while the VR pick
    /// is inactive (callers must fall through to the original mouse path).
    /// </summary>
    public static bool TryGetGameRay(out Vector3 origin, out Vector3 direction, out float maxDistance)
    {
        EnsureFresh();
        origin = _rayOrigin;
        direction = _rayDirection;
        maxDistance = _rayMaxDistance;
        return _source != PickSource.None;
    }

    /// <summary>
    /// Screen-space projection of the current pick point through the game camera
    /// (optionally snapped to the hex center, <c>[Board] SnapToHexCenter</c>).
    /// A miss projects far off-screen so downstream screen-ray consumers miss too.
    /// </summary>
    public static bool TryGetCursorScreenPoint(out Vector2 screenPoint)
    {
        EnsureFresh();
        screenPoint = OffscreenCursor;
        if (_source == PickSource.None)
            return false;

        if (!_hasHit)
            return true; // VR owns the cursor, but points at nothing.

        Camera? camera = VRRigDriver.HeadCamera;
        if (camera == null)
            camera = Camera.main;
        if (camera == null)
            return false;

        Vector3 projected = camera.WorldToScreenPoint(_cursorWorld);
        if (projected.z >= 0f)
            screenPoint = new Vector2(projected.x, projected.y);
        return true;
    }

    /// <summary>
    /// P5 (MISSION A.1): the world-space cursor point of the current pick — the hit
    /// point, snapped to the hovered hex center when <c>[Board] SnapToHexCenter</c> is
    /// on. False while the pick is inactive or missing. Feeds the GAME cursor
    /// projection only (test #14 item 2: the VISIBLE beam/reticle stay on the straight
    /// aim ray; the snapped hex shows via the game's own hex hover highlight).
    /// </summary>
    public static bool TryGetCursorWorld(out Vector3 world)
    {
        EnsureFresh();
        world = _cursorWorld;
        return _source != PickSource.None && _hasHit;
    }

    /// <summary>Hot-reload hygiene (module Shutdown).</summary>
    public static void Reset()
    {
        _frame = -1;
        _source = PickSource.None;
        _inScenario = false;
        _hand = null;
        _hasHit = false;
        _hitCollider = null;
    }

    // ---- per-frame compute ---------------------------------------------------------------

    private static void EnsureFresh()
    {
        if (Time.frameCount == _frame)
            return;
        _frame = Time.frameCount;
        Compute();
    }

    private static void Compute()
    {
        _source = PickSource.None;
        _inScenario = false;
        _hand = null;
        _hasHit = false;
        _hitCollider = null;

        // Vanilla passthrough outside the scenario and behind modal locks.
        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode == VRMode.Menu2D || mode == VRMode.ModalUI)
            return;

        // Verified vs real GH.Runtime.dll (ilspycmd 8.2, 2026-07-15):
        //   public static Controller Instance => _instance;          // Controller.cs:62
        //   public LayerMask m_ActiveSelectionRaycastLayer;          // Controller.cs:52
        //   (set to m_HexSelectionRaycastLayer in Controller.Start)
        Controller? controller = Controller.Instance;
        if (controller == null)
            return;

        // Past the mode + Controller gate: we are in a live scenario. Record it even if no hand
        // produces a pick below (source stays None) so HexHoverClear can still kill a stale star.
        _inScenario = true;
        int mask = controller.m_ActiveSelectionRaycastLayer.value;

        if (!BoardConfig.ForceFarMode.Value)
        {
            TryNearPick(VRHands.Left, mask);
            TryNearPick(VRHands.Right, mask);
        }

        if (_source == PickSource.None)
            TryFarPick(mask);

        if (_hasHit)
            ResolveCursorWorld();
    }

    private static void TryNearPick(VRHand? hand, int mask)
    {
        // Near-touch follows the Poke interactor's mode policy (BoardTargeting: Ray+Poke).
        if (hand == null || !hand.HasPose || !hand.Poke.Enabled)
            return;

        float scale = hand.WorldScale;
        float lift = NearOriginLift * scale;
        float range = Mathf.Max(0.01f, BoardConfig.TouchRange.Value) * scale;
        Vector3 origin = hand.Rig.IndexTip.position + Vector3.up * lift;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, lift + range, mask))
            return;

        float surface = hit.distance - lift; // fingertip-to-surface (negative = pressed in)
        if (_source == PickSource.Near && surface >= _nearSurfaceDistance)
            return; // keep the closer hand

        _source = PickSource.Near;
        _hand = hand;
        _rayOrigin = origin;
        _rayDirection = Vector3.down;
        _rayMaxDistance = lift + range;
        _hasHit = true;
        _hitPoint = hit.point;
        _hitCollider = hit.collider;
        _nearSurfaceDistance = surface;
    }

    private static void TryFarPick(int mask)
    {
        // Primary hand's ray (INTERFACES-P2: VRHands.PrimaryPick). TryGetPick is false
        // while the mode policy disables the ray or the hand is untracked.
        VRHand? hand = VRHands.Primary;
        if (hand == null || !hand.Ray.TryGetPick(out PickPose pick))
            return;

        _source = PickSource.Far;
        _hand = hand;
        _rayOrigin = pick.Origin;
        _rayDirection = pick.Direction;
        _rayMaxDistance = FarMaxDistance;

        // Authoritative raycast on the game's selection mask at the game's 1000f range
        // (the RayInteractor's own hit uses its visual range/mask; don't trust it here).
        if (Physics.Raycast(_rayOrigin, _rayDirection, out RaycastHit hit, FarMaxDistance, mask))
        {
            _hasHit = true;
            _hitPoint = hit.point;
            _hitCollider = hit.collider;
        }
    }

    private static void ResolveCursorWorld()
    {
        _cursorWorld = _hitPoint;
        if (!BoardConfig.SnapToHexCenter.Value || _hitCollider == null)
            return;

        // Hex center = the tile GameObject's position. The analytic converter
        //   ScenarioRuleLibrary.MF.ArrayIndexToCartesianCoord(Point arrayIndex,
        //       float xScalar, float yScalar, out float x, out float y)   [verified]
        // yields positive-map-space coords, NOT world space (BOARD-INPUT §2) — the
        // game itself reads world positions from the tile GameObject, so we do too.
        // Verified vs real GH.Runtime.dll: public CClientTile m_ClientTile; (TileBehaviour.cs:14)
        //   public GameObject m_GameObject; (CClientTile.cs:7)
        TileBehaviour? tile = _hitCollider.GetComponentInParent<TileBehaviour>();
        if (tile != null && tile.m_ClientTile != null && tile.m_ClientTile.m_GameObject != null)
            _cursorWorld = tile.m_ClientTile.m_GameObject.transform.position;
    }
}

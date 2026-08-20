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
///   GRIP-GATED (see <see cref="TryNearPick"/>): it only exists while that hand holds
///   the grip button.
/// - **Far**: the primary hand's <see cref="RayInteractor"/> pick
///   (<see cref="VRHands.PrimaryPick"/> per INTERFACES-P2 §2) — including the
///   [Dev] SimulateHands fallback, which drives the same RayInteractor.
///
/// LASER vs FINGERTIP ARBITRATION (user requirement 2026-08, "the laser must not
/// double-commit what the finger committed"). It is decided HERE, once, by the
/// near-over-far order below, and <see cref="BoardClickDriver"/> inherits it for free
/// because it switches on <see cref="Source"/>: exactly one source exists per frame, so
/// exactly one commit path runs. Concretely — while a hand holds the grip AND its
/// fingertip is inside <c>[Board] TouchRange</c> of the board, the FINGERTIP owns the
/// pick and the trigger cannot commit anything on the board (the far branch is never
/// reached). Take the finger out of range, or let go of the grip, and the far ray is
/// the pick again on the very next frame. The visible beam is untouched by this — only
/// which pick the game sees, and therefore which commit path can fire.
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

        // Vanilla passthrough outside the scenario only (no board exists in the 2D menu).
        // ModalUI deliberately does NOT bail any more (user ruling 2026-08: the laser
        // collides in every phase): the pick, the cursor projection and the game's own hex
        // hover highlight stay live under blocking modals — only the CLICK commit is
        // modal-gated, per target, in BoardClickDriver.RequestClick (decision table:
        // WorldUI.ModalFallback.HardCommitLockActive).
        // ModBuild 178: ASK FOR THE BOARD, not for "not the menu". This class is entirely about hex
        // tiles — it raycasts Controller.m_ActiveSelectionRaycastLayer and sets _inScenario below —
        // so the question it always meant is "does a scenario board exist". While Menu2D was the
        // exact complement of that, the mode test was an accurate shorthand; it stopped being one
        // when the 3D map room started resolving to TableIdle. A map table is not a hex board and
        // must not be picked as one.
        if (!VRModeStateMachine.ScenarioBoardExists)
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

        // Direct fingertip touch on hexes — [Board] TouchTilesWithFingertip is the SINGLE switch
        // (default on; the deleted [Board] ForceFarMode override, and why leaving it in shipped
        // this feature dead, are on BoardConfig.Bind). The grip gate in TryNearPick is what makes
        // the near pick safe now.
        if (BoardConfig.TouchTilesWithFingertip.Value)
        {
            TryNearPick(VRHands.Left, mask);
            TryNearPick(VRHands.Right, mask);
        }

        if (_source == PickSource.None)
            TryFarPick(mask);

        if (_hasHit)
            ResolveCursorWorld();
    }

    /// <summary>
    /// The fingertip pick of one hand — the near half of the near-over-far arbitration.
    ///
    /// <para>SAFETY GATE (explicit user requirement, 2026-08): the fingertip only picks while
    /// that hand HOLDS THE GRIP — "Faust mit ausgestrecktem Zeigefinger". A hand drifting over
    /// the board with the grip open is inert; it neither steals the pick from the laser nor can
    /// it commit anything, so an accidental brush across the diorama is impossible. The gesture
    /// is also self-illustrating: grip held with the trigger released is exactly
    /// <see cref="HandPose.Point"/>, which the FingerCurler renders as a fist with the index
    /// finger extended — the player's hand SHOWS the mode it is in.</para>
    ///
    /// <para>A hand that is HOLDING something is excluded as well: the grip is what grabs world
    /// panels and control boards (<c>IGrabbable.GrabWithGrip</c>), so a held object means the
    /// grip was pressed to carry it, not to touch a hex. Without this a player could not drag a
    /// board across the diorama without clicking every hex it passed over. Mirrors the same
    /// <c>Grabber.Held == null</c> condition the far click already carries.</para>
    /// </summary>
    private static void TryNearPick(VRHand? hand, int mask)
    {
        // Near-touch follows the Poke interactor's mode policy (BoardTargeting: Ray+Poke).
        if (hand == null || !hand.HasPose || !hand.Poke.Enabled)
            return;

        // The two gates above (see the doc comment): deliberate gesture, empty hand.
        if (!hand.GripPressed || hand.Grabber.Held != null)
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

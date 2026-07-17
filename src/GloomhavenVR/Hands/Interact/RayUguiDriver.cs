using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Far-ray uGUI pointing (P6, hardware test #8): the dominant hand's aim ray drives
/// hover/press/click on every world-space canvas registered in
/// <see cref="UguiPokeSurfaces"/> — the settings panel, converted dialogs and all
/// other world panels become laser-clickable exactly like the flat screen.
///
/// Mechanism: intersect the aim ray with each registered canvas plane (front side,
/// rect bounds); the nearest hit drives the existing <see cref="UguiPointer"/>
/// (ExecuteEvents with modality respect — a disabled GraphicRaycaster yields no
/// events) with its own pointer ID, and feeds the hit point into
/// <see cref="RayInteractor.UiHitOverride"/> so the visible beam clamps to the panel.
/// Trigger = press/release/click; the pressed canvas is latched (FlatScreen pattern)
/// so small drifts during the pull cannot cancel the click.
///
/// Priority rules:
/// - A nearer PHYSICS hit on the same ray (fan card, miniature, furniture) blocks the
///   UI hit — no clicking through objects.
/// - While this driver (or the fan laser) clamps the beam, <c>Ray.HasFreshUiHit</c> is
///   true and BoardClickDriver skips its far trigger-click — UI wins over board picks.
/// - The 2D flat screen keeps its own path (screen-space canvas, never registered in
///   UguiPokeSurfaces).
///
/// One instance per hand (created by <see cref="VRHand.Initialize"/>, ticked after the
/// ray); inert unless the hand is the dominant one and its ray interactor is enabled.
/// No per-frame allocations: for-loops over the surface registry, reused event data.
/// </summary>
internal sealed class RayUguiDriver
{
    /// <summary>Max pointing distance in meters (scale 1) — matches the ray interactor.</summary>
    private const float MaxDistanceMeters = 20f;

    /// <summary>A physics hit closer than the UI plane by more than this blocks the UI hit (meters, scale 1).</summary>
    private const float OcclusionEpsilonMeters = 0.005f;

    private readonly VRHand _hand;
    private readonly UguiPointer _pointer;

    private Canvas? _canvas;   // hovered canvas; latched while pressing
    private bool _pressing;

    internal RayUguiDriver(VRHand hand)
    {
        _hand = hand;
        _pointer = new UguiPointer(hand.Side, farRay: true);
    }

    /// <summary>True while the aim ray hits a registered UI surface this frame.</summary>
    public bool HasHit { get; private set; }

    /// <summary>Ray distance to the UI surface hit (valid while <see cref="HasHit"/>).</summary>
    public float HitDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>Currently hovered uGUI object under the ray, if any.</summary>
    public GameObject? Hovered => _pointer.Hovered;

    internal void Tick()
    {
        // Dominant hand only (the off-hand holds the fan); dominance can switch live.
        if (!_hand.HasPose || !_hand.Ray.Enabled || VRHands.Primary != _hand)
        {
            Cancel();
            return;
        }

        PickPose pick = _hand.Ray.Current;
        float scale = _hand.WorldScale;

        if (_pressing && _canvas != null)
        {
            TickPressed(pick, scale);
            return;
        }
        _pressing = false;

        Canvas? best = null;
        float bestDist = MaxDistanceMeters * scale;
        Vector3 bestPoint = default;
        bool sawDead = false;

        var surfaces = UguiPokeSurfaces.Surfaces;
        for (int i = 0; i < surfaces.Count; i++)
        {
            Canvas canvas = surfaces[i];
            if (canvas == null)
            {
                sawDead = true;
                continue;
            }
            if (!canvas.isActiveAndEnabled)
                continue;
            LogCanvasOnce(canvas);
            if (TryIntersect(canvas, pick.Origin, pick.Direction, bestDist, out float dist, out Vector3 point))
            {
                best = canvas;
                bestDist = dist;
                bestPoint = point;
            }
        }

        if (sawDead)
            UguiPokeSurfaces.Prune();

        // Physics occlusion: something solid in front of the panel blocks the laser.
        if (best != null && pick.HasHit && pick.HitDistance < bestDist - OcclusionEpsilonMeters * scale)
            best = null;

        if (!ReferenceEquals(best, _canvas))
        {
            _pointer.Cancel();
            _canvas = best;
        }

        if (_canvas == null)
        {
            HasHit = false;
            HitDistance = float.PositiveInfinity;
            return;
        }

        HasHit = true;
        HitDistance = bestDist;
        _hand.Ray.UiHitOverride = bestPoint; // beam clamps to the panel (one-frame latch)

        Vector2 screenPos = ToScreen(_canvas, bestPoint);
        bool hit = _pointer.TryRaycast(_canvas, screenPos, out RaycastResult top);
        GameObject? previous = _pointer.Hovered;
        _pointer.SetHovered(hit ? top.gameObject : null);
        if (_pointer.Hovered != null && !ReferenceEquals(_pointer.Hovered, previous))
            _hand.SendHaptic(HapticPreset.HoverTick); // debounced: only on hover change

        if (_hand.TriggerDown && hit && _hand.Grabber.Held == null)
        {
            _pressing = true;
            _pointer.Press(screenPos);
            _hand.SendHaptic(HapticPreset.ClickPulse);
        }
    }

    /// <summary>
    /// Latched press (FlatScreen pattern): keep driving the pressed canvas until the
    /// trigger releases, clamping the ray's plane hit into the canvas rect so drift
    /// off the edge cannot orphan the press.
    /// </summary>
    private void TickPressed(in PickPose pick, float scale)
    {
        Canvas canvas = _canvas!;
        if (canvas == null || !canvas.isActiveAndEnabled)
        {
            Cancel();
            return;
        }

        var rect = (RectTransform)canvas.transform;
        Transform t = rect;
        float denom = Vector3.Dot(pick.Direction, t.forward);
        Vector3 point;
        if (Mathf.Abs(denom) > 1e-5f)
        {
            float dist = Vector3.Dot(t.position - pick.Origin, t.forward) / denom;
            point = pick.Origin + pick.Direction * Mathf.Max(0f, dist);
            HitDistance = Mathf.Max(0f, dist);
        }
        else
        {
            point = t.position;
            HitDistance = Vector3.Distance(pick.Origin, point);
        }

        // Clamp into the rect (local space) so events stay on the panel.
        Vector3 local = t.InverseTransformPoint(point);
        Rect r = rect.rect;
        local.x = Mathf.Clamp(local.x, r.xMin, r.xMax);
        local.y = Mathf.Clamp(local.y, r.yMin, r.yMax);
        local.z = 0f;
        point = t.TransformPoint(local);

        HasHit = true;
        _hand.Ray.UiHitOverride = point;

        Vector2 screenPos = ToScreen(canvas, point);
        bool hit = _pointer.TryRaycast(canvas, screenPos, out RaycastResult top);
        _pointer.SetHovered(hit ? top.gameObject : null);

        // Trigger STATE, not the up edge — a mode/hands hiccup must still release.
        if (!_hand.TriggerPressed)
        {
            _pressing = false;
            _pointer.Release(screenPos);
        }
    }

    // Reused corner buffer (GetWorldCorners fills in place — no per-frame allocations).
    private static readonly Vector3[] Corners = new Vector3[4];

    // One-time world-rect log per registered canvas (instance IDs; survives re-registration).
    private static readonly HashSet<int> LoggedCanvases = new();

    /// <summary>
    /// Ray ∩ canvas via the RectTransform's actual WORLD-SPACE corners (test #13):
    /// pivot/sizeDelta assumptions do not enter — the plane is spanned by the real
    /// corners (GetWorldCorners: 0=bottom-left, 1=top-left, 2=top-right,
    /// 3=bottom-right), so converted windows (host rect + re-anchored child, e.g.
    /// the story window at 1920x1080 with host scale 0.7) intersect over their
    /// ENTIRE surface, including under parent shear / negative scale.
    /// </summary>
    private static bool TryIntersect(Canvas canvas, Vector3 origin, Vector3 direction,
        float maxDist, out float dist, out Vector3 point)
    {
        dist = 0f;
        point = default;

        var rect = (RectTransform)canvas.transform;
        rect.GetWorldCorners(Corners);
        Vector3 right = Corners[3] - Corners[0]; // world-space +X edge
        Vector3 up = Corners[1] - Corners[0];    // world-space +Y edge
        float rightLen2 = right.sqrMagnitude;
        float upLen2 = up.sqrMagnitude;
        if (rightLen2 < 1e-12f || upLen2 < 1e-12f)
            return false; // degenerate rect (zero size / not laid out yet)

        // uGUI faces -normal (viewer side); a ray coming FROM the viewer side travels
        // along +normal: require denom > 0 (back-side pointing never hits).
        Vector3 normal = Vector3.Cross(right, up).normalized; // == canvas forward
        float denom = Vector3.Dot(direction, normal);
        if (denom < 1e-5f)
            return false;

        dist = Vector3.Dot(Corners[0] - origin, normal) / denom;
        if (dist <= 0f || dist >= maxDist)
            return false;

        point = origin + direction * dist;
        Vector3 d = point - Corners[0];
        float u = Vector3.Dot(d, right) / rightLen2;
        float v = Vector3.Dot(d, up) / upLen2;
        return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
    }

    /// <summary>One-time verification log per canvas: its actual world rect (test #13).</summary>
    private static void LogCanvasOnce(Canvas canvas)
    {
        if (!LoggedCanvases.Add(canvas.GetInstanceID()))
            return;
        var rect = (RectTransform)canvas.transform;
        rect.GetWorldCorners(Corners);
        float w = (Corners[3] - Corners[0]).magnitude;
        float h = (Corners[1] - Corners[0]).magnitude;
        Core.VRLog.Info("Interact",
            $"Ray-uGUI canvas '{canvas.name}': world rect {w:F3}x{h:F3} m, " +
            $"BL={Corners[0]:F3} TL={Corners[1]:F3} TR={Corners[2]:F3} BR={Corners[3]:F3}, " +
            $"pivot={rect.pivot}, sizeDelta={rect.sizeDelta}, lossyScale={rect.lossyScale:F4}.");
    }

    private static Vector2 ToScreen(Canvas canvas, Vector3 worldPoint)
    {
        Camera? cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        return RectTransformUtility.WorldToScreenPoint(cam, worldPoint);
    }

    /// <summary>Abort in-flight hover/press (hand lost, ray disabled, dominance switch).</summary>
    internal void Cancel()
    {
        if (_canvas != null || _pressing)
        {
            _pointer.Cancel();
            _canvas = null;
            _pressing = false;
        }
        HasHit = false;
        HitDistance = float.PositiveInfinity;
    }
}

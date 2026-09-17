using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    // The user explicitly permits opening-time room making (2026-09-17), superseding the old
    // fixed-window rule ONLY for an overlapping new map modal. This is not a lost-window recall:
    // no headset dwell, no follow mode, no periodic rearrangement, no restore when a window closes.
    private const int ReflowCapacity = 16;
    private const float ReflowSeconds = 0.26f;
    private static readonly WindowPanel?[] ReflowWindows = new WindowPanel?[ReflowCapacity];
    private static readonly WindowReflowLayout.Window[] ReflowGeometry = new WindowReflowLayout.Window[ReflowCapacity];
    private static readonly bool[] ReflowIncluded = new bool[ReflowCapacity];
    private static readonly float[] ReflowTargetX = new float[ReflowCapacity];
    private static readonly Vector3[] ReflowCentres = new Vector3[ReflowCapacity];
    private static readonly Vector3[] ReflowFrom = new Vector3[ReflowCapacity];
    private static readonly Vector3[] ReflowTo = new Vector3[ReflowCapacity];
    private static readonly Quaternion[] ReflowFromRotation = new Quaternion[ReflowCapacity];
    private static readonly Quaternion[] ReflowToRotation = new Quaternion[ReflowCapacity];
    private static readonly int[] ReflowRevision = new int[ReflowCapacity];
    private static readonly SharedWindowKind[] ReflowKinds = new SharedWindowKind[ReflowCapacity];
    private static readonly Vector3[] ReflowCorners = new Vector3[4];
    private static readonly Vector3[,] ReflowFootprints = new Vector3[ReflowCapacity, 4];
    private static readonly Rect[] ReflowViewports = new Rect[ReflowCapacity];
    private static int _reflowCount;
    private static float _reflowStarted;
    private static bool _reflowRunning;

    internal static bool AnySharedReflowActive => _reflowRunning;

    internal static bool SharedReflowActive(SharedWindowKind kind)
    {
        if (!_reflowRunning) return false;
        for (int i = 0; i < _reflowCount; i++)
            if (ReflowIncluded[i] && ReflowKinds[i] == kind) return true;
        return false;
    }

    private static bool ReflowKind(SharedWindowKind kind) => kind == SharedWindowKind.MapStory
        || kind == SharedWindowKind.Encounter || kind == SharedWindowKind.QuestConfirm;

    private static bool ReflowVisible(WindowPanel wp) => wp.Panel.IsAlive && wp.Window != null
        && !wp.Dormant && !wp.UserClosing && !wp.HoverCard && wp.Grab != null
        && wp.Panel.HostGo.activeInHierarchy && !wp.Panel.RenderHidden
        && !wp.Panel.OwnerRenderHidden && !wp.Panel.RevealPending
        && ((IPanelGrabOwner)wp.Grab).GrabVisible;

    private static void TickWindowRoomMaking()
    {
        if (!MapRoom.MapRoomDriver.Active)
        {
            CancelWindowRoomMaking();
            return;
        }
        if (_reflowRunning)
        {
            TickRoomMakingAnimation();
            return;
        }
        bool? authority = null;
        float now = Time.unscaledTime;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!ReflowKind(SharedWindows.KindOf(wp.Window))) continue;
            if (wp.ReflowOpenedAt < 0f) wp.ReflowOpenedAt = now;
            float age = now - wp.ReflowOpenedAt;
            if (wp.ReflowCancelled || wp.ReflowFinalFitTried || age > 3f) continue;
            if (wp.Grab != null && (wp.Grab.IsGrabbed || wp.Grab.UserMoved || wp.Grab.PeerPlaced))
            {
                wp.ReflowCancelled = true;
                continue;
            }
            // Native first-fit sometimes times out while the window still opens correctly. Never
            // wait forever for FitMeasuredOnce, and never add a reveal or gameplay continuation gate.
            bool fitted = wp.Panel.FitMeasuredOnce;
            if (!ReflowVisible(wp) || age < 0.1f || (!fitted && age < 0.65f)) continue;
            authority ??= !SharedWindows.SessionIsOnline() || (SharedWindowReflowBridge.CanArrange?.Invoke() ?? false);
            if (!authority.Value) continue;
            if (wp.ReflowTried && !fitted) continue;
            if (now < wp.ReflowNextTry) continue;
            if (TryStartWindowRoomMaking(wp, out bool retry))
            {
                wp.ReflowTried = true;
                wp.ReflowFinalFitTried = fitted;
                return;
            }
            if (retry) wp.ReflowNextTry = now + 0.1f;
            else
            {
                wp.ReflowTried = true;
                wp.ReflowFinalFitTried = fitted;
            }
        }
    }

    private static bool TryStartWindowRoomMaking(WindowPanel incoming, out bool retry)
    {
        retry = false;
        Camera? camera = Rig.VRRigDriver.HeadCamera;
        if (camera == null) return false;
        Vector3 eye = camera.transform.position;
        Vector3 forward = camera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return false;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        int incomingIndex = -1;
        _reflowCount = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!ReflowVisible(wp)) continue;
            if (_reflowCount == ReflowCapacity) return false;
            int n = _reflowCount++;
            ReflowWindows[n] = wp;
            SharedWindowKind kind = SharedWindows.KindOf(wp.Window);
            ReflowKinds[n] = kind;
            ReflowGeometry[n] = MeasureReflowWindow(wp, eye, forward, right, out ReflowCentres[n]);
            for (int c = 0; c < 4; c++) ReflowFootprints[n, c] = ReflowCorners[c];
            bool inView = ReadReflowViewport(camera, n, out ReflowViewports[n]);
            bool corner = ReferenceEquals(_questCornerPanel, wp.Panel) && _questCornerTaken;
            ReflowGeometry[n].Movable = inView && ReflowKind(kind) && !corner && !wp.Grab!.IsGrabbed;
            if (ReferenceEquals(incoming, wp))
            {
                if (!inView) return false;
                incomingIndex = n;
            }
        }
        float scale = MapRoom.MapRoomDriver.TryGetParchmentFrame(out _, out float roomScale)
            ? Mathf.Max(roomScale, 0.01f) : Mathf.Max(PanelLayout.WorldScale, 0.01f);
        // The measured comfortable view is capped at 70 degrees. Wider
        // native panels move slightly farther away instead of being shrunk or shunted off-screen.
        float halfView = Mathf.Tan(Mathf.Min(35f, UsableHalfConeDeg()) * Mathf.Deg2Rad);
        if (!WindowReflowLayout.TryArrange(ReflowGeometry, _reflowCount, incomingIndex, halfView,
                0.035f * scale, 3.5f * scale, ReflowIncluded, ReflowTargetX, out float depth))
        {
            if (WindowReflowLayout.HasVisibleOverlap(ReflowGeometry, _reflowCount, incomingIndex, halfView))
                VRLog.Note("WorldUI", $"WINDOW ROOM MAKING: '{incoming.Window.name}' overlaps, but no "
                    + "readable collision-free row fits. Keeping native UI interactive and its grab bars available.");
            return false;
        }
        bool online = SharedWindows.SessionIsOnline();
        int claimed = 0;
        for (int i = 0; i < _reflowCount; i++)
        {
            if (!ReflowIncluded[i]) continue;
            SharedWindowKind kind = ReflowKinds[i];
            claimed++;
            WindowPanel wp = ReflowWindows[i]!;
            Transform frame = ((IPanelGrabOwner)wp.Grab!).GrabRoot!;
            wp.Grab.ReadRoomMakingStart(out ReflowFrom[i], out ReflowFromRotation[i]);
            // Keep vertical position and scale. Width includes the full native hit rect, not just
            // visible ink, so the transparent canvas cannot keep intercepting the other window.
            Vector3 target = eye + forward * depth + right * ReflowTargetX[i];
            target.y = ReflowCentres[i].y;
            ReflowToRotation[i] = Quaternion.LookRotation(forward, Vector3.up);
            ReflowTo[i] = WindowReflowPose.FramePositionForCentre(frame.position, frame.rotation,
                ReflowCentres[i], target, ReflowToRotation[i]);
            Quaternion delta = ReflowToRotation[i] * Quaternion.Inverse(frame.rotation);
            for (int c = 0; c < 4; c++)
                ReflowFootprints[i, c] = ReflowTo[i] + delta * (ReflowFootprints[i, c] - frame.position);
            if (!ReadReflowViewport(camera, i, out Rect destination)) return false;
            Rect original = ReflowViewports[i];
            // The horizontal solver is yaw-only; the actual headset can be looking up/down. Never
            // solve its horizontal overlap by moving an originally visible edge out of that view.
            if ((original.xMin >= 0f && destination.xMin < -0.01f)
                || (original.xMax <= 1f && destination.xMax > 1.01f)
                || (original.yMin >= 0f && destination.yMin < -0.01f)
                || (original.yMax <= 1f && destination.yMax > 1.01f)) return false;
            ReflowRevision[i] = online ? (SharedWindowReflowBridge.Revision?.Invoke(kind) ?? 0) : 0;
        }
        for (int i = 0; i < _reflowCount; i++)
        {
            if (!ReflowIncluded[i] || !online) continue;
            if (SharedWindowReflowBridge.Begin?.Invoke(ReflowKinds[i]) ?? false)
            {
                ReflowRevision[i] = SharedWindowReflowBridge.Revision?.Invoke(ReflowKinds[i]) ?? 0;
                continue;
            }
            retry = true;
            for (int j = 0; j < i; j++)
                if (ReflowIncluded[j]) SharedWindowReflowBridge.End?.Invoke(ReflowKinds[j]);
            return false;
        }
        _reflowStarted = Time.unscaledTime;
        _reflowRunning = true;
        VRLog.Note("WorldUI", $"WINDOW ROOM MAKING: '{incoming.Window.name}' opens into an overlap; "
            + $"animating {claimed} windows over {ReflowSeconds:F2}s, depth {depth / scale:F2}m. "
            + "One author, existing shared pose stream, no native UI callbacks.");
        return true;
    }

    private static bool ReadReflowViewport(Camera camera, int index, out Rect bounds)
    {
        float left = float.PositiveInfinity, bottom = float.PositiveInfinity;
        float right = float.NegativeInfinity, top = float.NegativeInfinity;
        bounds = default;
        for (int c = 0; c < 4; c++)
        {
            Vector3 view = camera.WorldToViewportPoint(ReflowFootprints[index, c]);
            if (view.z <= 0f) return false;
            left = Mathf.Min(left, view.x); right = Mathf.Max(right, view.x);
            bottom = Mathf.Min(bottom, view.y); top = Mathf.Max(top, view.y);
        }
        bounds = Rect.MinMaxRect(left, bottom, right, top);
        return right > 0f && left < 1f && top > 0f && bottom < 1f;
    }

    private static WindowReflowLayout.Window MeasureReflowWindow(WindowPanel wp, Vector3 eye,
        Vector3 forward, Vector3 right, out Vector3 worldCentre)
    {
        RectTransform rect = wp.Panel.HostRect;
        Rect bounds = rect.rect;
        if (CanvasConversion.TryGetHitRect(wp.Panel.HostCanvas, out Rect hit)
            && hit.width > 0f && hit.height > 0f)
            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, hit.xMin), Mathf.Min(bounds.yMin, hit.yMin),
                Mathf.Max(bounds.xMax, hit.xMax), Mathf.Max(bounds.yMax, hit.yMax));
        ReflowCorners[0] = rect.TransformPoint(new Vector3(bounds.xMin, bounds.yMin, 0f));
        ReflowCorners[1] = rect.TransformPoint(new Vector3(bounds.xMin, bounds.yMax, 0f));
        ReflowCorners[2] = rect.TransformPoint(new Vector3(bounds.xMax, bounds.yMax, 0f));
        ReflowCorners[3] = rect.TransformPoint(new Vector3(bounds.xMax, bounds.yMin, 0f));
        worldCentre = rect.TransformPoint(bounds.center);
        var result = new WindowReflowLayout.Window
        {
            Left = float.PositiveInfinity, Right = float.NegativeInfinity,
            Bottom = float.PositiveInfinity, Top = float.NegativeInfinity,
            Width = rect.TransformVector(Vector3.right * bounds.width).magnitude
        };
        Vector3 centre = worldCentre - eye;
        result.X = Vector3.Dot(centre, right);
        result.Depth = Vector3.Dot(centre, forward);
        for (int i = 0; i < 4; i++)
        {
            Vector3 relative = ReflowCorners[i] - eye;
            float z = Vector3.Dot(relative, forward);
            if (z <= 0.01f) { result.Depth = -1f; return result; }
            float x = Vector3.Dot(relative, right) / z;
            float y = relative.y / z;
            result.Left = Mathf.Min(result.Left, x);
            result.Right = Mathf.Max(result.Right, x);
            result.Bottom = Mathf.Min(result.Bottom, y);
            result.Top = Mathf.Max(result.Top, y);
        }
        return result;
    }

    private static void TickRoomMakingAnimation()
    {
        bool online = SharedWindows.SessionIsOnline();
        if (online && !(SharedWindowReflowBridge.CanArrange?.Invoke() ?? false))
        {
            CancelWindowRoomMaking();
            return;
        }
        for (int i = 0; i < _reflowCount; i++)
        {
            if (!ReflowIncluded[i]) continue;
            WindowPanel? wp = ReflowWindows[i];
            if (wp == null || !Converted.Contains(wp) || !ReflowVisible(wp) || wp.Grab!.IsGrabbed
                || SharedWindows.KindOf(wp.Window) != ReflowKinds[i]
                || (online && (SharedWindowReflowBridge.Revision?.Invoke(ReflowKinds[i]) ?? 0) != ReflowRevision[i]))
            {
                CancelWindowRoomMaking();
                return;
            }
        }
        float t = Mathf.Clamp01((Time.unscaledTime - _reflowStarted) / ReflowSeconds);
        float eased = t * t * (3f - 2f * t);
        for (int i = 0; i < _reflowCount; i++)
        {
            if (!ReflowIncluded[i]) continue;
            WindowPanel wp = ReflowWindows[i]!;
            wp.Grab!.PlaceRoomMakingSample(Vector3.Lerp(ReflowFrom[i], ReflowTo[i], eased),
                Quaternion.Slerp(ReflowFromRotation[i], ReflowToRotation[i], eased));
        }
        if (t >= 1f) CancelWindowRoomMaking(completed: true);
    }

    private static void CancelWindowRoomMaking(bool completed = false)
    {
        if (!_reflowRunning)
        {
            System.Array.Clear(ReflowWindows, 0, ReflowWindows.Length);
            _reflowCount = 0;
            return;
        }
        _reflowRunning = false;
        VRLog.Note("WorldUI", "WINDOW ROOM MAKING: " + (completed
            ? "animation completed; positions stay fixed until the next explicit action."
            : "animation cancelled by a grab, window lifetime or shared authority change; no pose restored."));
        for (int i = 0; i < _reflowCount; i++)
        {
            if (!ReflowIncluded[i]) continue;
            WindowPanel? wp = ReflowWindows[i];
            if (wp != null && !completed) wp.ReflowCancelled = true;
            if (SharedWindows.SessionIsOnline()) SharedWindowReflowBridge.End?.Invoke(ReflowKinds[i]);
            ReflowWindows[i] = null;
        }
        System.Array.Clear(ReflowWindows, 0, ReflowWindows.Length);
        _reflowCount = 0;
        RefreshStandingArcClaims(-1);
    }
}

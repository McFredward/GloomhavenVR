using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Opening-time paper placement from Frosthaven's enemy reveal. Board and
/// panel footprints share the viewer's projection, so separation in world
/// depth cannot disguise a board covering the cards.
/// </summary>
internal sealed class EnemyRevealPlacement
{
    private const float Near = 0.025f;
    private const float ViewMargin = 0.9f;
    private const float SideStep = 10f;
    private const float LiftStep = 3f;
    private const float DepthStep = 0.08f;
    private const int SideSteps = 3;
    private const int LiftSteps = 14;
    private const int DepthSteps = 4;

    private readonly List<(Rect Area, bool Own)> _boards = new(4);
    private readonly Vector3[] _corners = new Vector3[8];
    private Vector3 _eye, _right, _up, _forward;
    private Rect _view;
    private float _yaw, _scale;

    internal void Begin(Vector3 eye, Vector3 gaze, float scale, Vector2 viewHalfTangents)
    {
        _boards.Clear();
        _eye = eye;
        _scale = Mathf.Max(0.01f, scale);
        _yaw = Mathf.Atan2(gaze.x, gaze.z) * Mathf.Rad2Deg;
        Quaternion facing = Quaternion.Euler(0f, _yaw, 0f);
        _right = facing * Vector3.right;
        float pitch = Mathf.Atan2(gaze.y, new Vector2(gaze.x, gaze.z).magnitude) * Mathf.Rad2Deg;
        _forward = Quaternion.AngleAxis(-Mathf.Clamp(pitch, -75f, 75f), _right) * (facing * Vector3.forward);
        _up = Vector3.Cross(_forward, _right);
        Vector2 visible = viewHalfTangents * ViewMargin;
        _view = Rect.MinMaxRect(-visible.x, -visible.y, visible.x, visible.y);
    }

    internal static Vector2 DefaultViewHalfTangents => new(Mathf.Tan(44f * Mathf.Deg2Rad), Mathf.Tan(35f * Mathf.Deg2Rad));

    internal static Vector2 StereoViewHalfTangents(Matrix4x4 left, Matrix4x4 right)
    {
        float x = Mathf.Min(HalfTangent(left.m00, left.m02), HalfTangent(right.m00, right.m02));
        float y = Mathf.Min(HalfTangent(left.m11, left.m12), HalfTangent(right.m11, right.m12));
        Vector2 fallback = DefaultViewHalfTangents;
        return new(x > 0f ? x : fallback.x, y > 0f ? y : fallback.y);
    }

    private static float HalfTangent(float diagonal, float offset)
    {
        if (diagonal <= 0.0001f || float.IsInfinity(diagonal)) return 0f;
        float limit = (1f - Mathf.Abs(offset)) / diagonal;
        return limit > 0.001f && limit < 10f ? limit : 0f;
    }

    internal void AddBoard(Bounds bounds, Matrix4x4 localToWorld, float clearance, bool own)
    {
        // Pad the true oriented board towards world-up, in the reader's real metres.
        // The far/near outline includes every corner and clips edges crossing the eye.
        Vector3 low = bounds.min, high = bounds.max;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < _corners.Length; i++)
        {
            bool top = (i & 2) != 0;
            Vector3 corner = new((i & 1) == 0 ? low.x : high.x,
                top ? high.y : low.y, (i & 4) == 0 ? low.z : high.z);
            Vector3 offset = (localToWorld.MultiplyPoint3x4(corner) - _eye) / _scale;
            if (top) offset.y += clearance;
            _corners[i] = ViewCoordinates(offset);
            if (_corners[i].z >= Near) Include(_corners[i], ref min, ref max);
        }
        for (int axis = 1; axis < _corners.Length; axis <<= 1)
        for (int i = 0; i < _corners.Length; i++)
        {
            if ((i & axis) != 0) continue;
            Vector3 a = _corners[i], b = _corners[i | axis];
            if ((a.z >= Near) == (b.z >= Near)) continue;
            Include(Vector3.LerpUnclamped(a, b, (Near - a.z) / (b.z - a.z)), ref min, ref max);
        }
        if (min.x < max.x && min.y < max.y)
            _boards.Add((Rect.MinMaxRect(min.x, min.y, max.x, max.y), own));
    }

    internal Vector3 Find(float distance, float drop, Vector2 halfSize, out string clearance)
    {
        Vector3 natural = _forward * distance - Vector3.up * drop;
        float bestScore = Score(natural, halfSize, out bool clear, out int blocker);
        string obstacle = blocker < 0 ? "none" : _boards[blocker].Own ? "own board" : "peer board";
        if (clear)
        {
            clearance = "preferred gaze footprint clear";
            return _eye + natural * _scale;
        }

        Vector3 best = natural;
        float basePitch = Mathf.Atan2(natural.y, new Vector2(natural.x, natural.z).magnitude) * Mathf.Rad2Deg;
        float baseDistance = natural.magnitude;
        for (int rung = 0; rung <= DepthSteps; rung++)
        for (int side = 0; side <= SideSteps * 2; side++)
        {
            float yaw = _yaw + ((side + 1) / 2) * (side % 2 == 0 ? -SideStep : SideStep);
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            for (int lift = 0; lift <= LiftSteps; lift++)
            {
                if (rung == 0 && side == 0 && lift == 0) continue;
                float pitch = Mathf.Min(78f, basePitch + lift * LiftStep) * Mathf.Deg2Rad;
                float depth = baseDistance + rung * DepthStep;
                Vector3 candidate = facing * new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)) * depth;
                float score = Score(candidate, halfSize, out clear, out _);
                if (clear)
                {
                    clearance = $"full footprint in view and clear (initial overlap: {obstacle})";
                    return _eye + candidate * _scale;
                }
                if (score <= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
        }
        clearance = $"best visible footprint in range (initial overlap: {obstacle})";
        return _eye + best * _scale;
    }

    private float Score(Vector3 center, Vector2 halfSize, out bool clear, out int blocker)
    {
        clear = false;
        blocker = -1;
        Quaternion facing = PanelPlacement.Facing(center, Vector3.zero);
        Vector3 across = facing * Vector3.right * halfSize.x;
        Vector3 up = Vector3.up * halfSize.y;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector3 point = ViewCoordinates(center + ((i & 1) == 0 ? -across : across) + ((i & 2) == 0 ? -up : up));
            if (point.z < Near) return -1f;
            Include(point, ref min, ref max);
        }
        Rect panel = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        Rect visible = Intersection(panel, _view);
        float area = panel.width * panel.height;
        if (area <= 0f) return -1f;
        float visibleArea = visible.width * visible.height;
        float covered = 0f, most = 0f;
        for (int i = 0; i < _boards.Count; i++)
        {
            Rect overlap = Intersection(visible, _boards[i].Area);
            float overlapArea = overlap.width * overlap.height;
            covered += overlapArea;
            if (overlapArea > most)
            {
                most = overlapArea;
                blocker = i;
            }
        }
        bool full = min.x >= _view.xMin && max.x <= _view.xMax && min.y >= _view.yMin && max.y <= _view.yMax;
        clear = full && covered == 0f;
        return Mathf.Max(0f, visibleArea - covered) / area;
    }

    private Vector3 ViewCoordinates(Vector3 offset) =>
        new(Vector3.Dot(offset, _right), Vector3.Dot(offset, _up), Vector3.Dot(offset, _forward));

    private static void Include(Vector3 point, ref Vector2 min, ref Vector2 max)
    {
        var projected = new Vector2(point.x, point.y) / point.z;
        min = Vector2.Min(min, projected);
        max = Vector2.Max(max, projected);
    }

    private static Rect Intersection(Rect a, Rect b)
    {
        float x = Mathf.Max(a.xMin, b.xMin), y = Mathf.Max(a.yMin, b.yMin);
        return new Rect(x, y, Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - x),
            Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - y));
    }
}

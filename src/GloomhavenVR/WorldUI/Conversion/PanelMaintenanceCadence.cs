using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Optional static-window size polling compromise. This never throttles native callbacks,
/// raycasters, reveal/ownership gates, grabs, sorting or rendered widget animations. A native
/// descendant-only size change can wait at most the selected interval; changing the root rect,
/// revealing or moving the panel immediately runs the original fit/hit-rect path again.
/// </summary>
internal static class PanelMaintenanceCadence
{
    internal struct State
    {
        internal bool HasFrame;
        internal float Next;
        internal Rect Rect;
        internal Vector3 Position, Scale;
        internal Quaternion Rotation;
        internal bool RenderHidden;
    }

    internal static bool Due(ref State state, RectTransform root, float now, float interval,
        bool immediate, bool hidden)
    {
        if (root == null) return true;
        Rect rect = root.rect;
        Vector3 position = root.localPosition, scale = root.localScale;
        Quaternion rotation = root.localRotation;
        bool changed = !state.HasFrame || state.Rect != rect || state.Position != position
            || state.Scale != scale || state.Rotation != rotation || state.RenderHidden != hidden;
        bool due = interval <= 0f || immediate || changed || now >= state.Next;
        if (!due) return false;
        state.HasFrame = true; state.Rect = rect; state.Position = position;
        state.Scale = scale; state.Rotation = rotation; state.RenderHidden = hidden;
        state.Next = now + Mathf.Clamp(interval, 0f, .2f);
        return true;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>One physical enemy-card seat for the original track and its observer copies.
/// The native popup holder is shared by every portrait, so its origin cannot locate a
/// per-portrait hover. Derive the seat from the actual displayed portrait instead, and
/// clear only the owning board's measured opaque rim. Ordinary depth testing then lets
/// every nearer hand, figure and other board occlude it. No viewer pose enters this math.</summary>
internal sealed class InitiativePopupPlacement
{
    private const float Gap = .006f;
    private const float RimClearance = .002f;
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly Dictionary<RectTransform, Vector3> _original = new();
    private Transform? _board;
    private MeshRenderer[] _surfaces = System.Array.Empty<MeshRenderer>();
    private int _boardChildren;
    private float _rim;

    internal static RectTransform? Portrait(InitiativeTrackEnemyBehaviour entry)
    {
        RawImage? image = entry.Avatar != null ? entry.Avatar.GetComponentInChildren<RawImage>() : null;
        return image != null ? image.rectTransform : null;
    }

    internal void Place(RectTransform popup, RectTransform portrait, Transform board)
    {
        if (popup == null || portrait == null || board == null || popup.parent == null) return;
        if (!_original.ContainsKey(popup)) _original.Add(popup, popup.localPosition);
        bool rebuild = !ReferenceEquals(_board, board) || _boardChildren != board.childCount || _surfaces.Length == 0;
        for (int i = 0; !rebuild && i < _surfaces.Length; i++) rebuild |= _surfaces[i] == null;
        if (rebuild) MeasureRim(board);

        portrait.GetWorldCorners(_corners);
        float left = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.PositiveInfinity;
        for (int i = 0; i < _corners.Length; i++)
        {
            Vector3 corner = board.InverseTransformPoint(_corners[i]);
            left = Mathf.Min(left, corner.x); right = Mathf.Max(right, corner.x);
            bottom = Mathf.Min(bottom, corner.y);
        }
        Vector3 top = board.InverseTransformPoint(popup.TransformPoint(new Vector3(popup.rect.center.x, popup.rect.yMax, 0f)));
        Vector3 seat = new((left + right) * .5f, bottom - Gap, Mathf.Min(top.z, _rim - RimClearance));
        Vector3 delta = board.TransformVector(seat - top);
        if (delta.sqrMagnitude > 1e-14f) popup.position += delta;
    }

    // The owner's complete draw band can change as boards move. A popup's extra
    // foreground tier is relative to that live band, never a one-time global order.
    internal static Canvas Order(GameObject popup, Canvas host, int hostTier, int popupTier)
    {
        Canvas? overlay = popup.GetComponent<Canvas>();
        if (overlay == null) overlay = popup.AddComponent<Canvas>();
        if (!overlay.overrideSorting) overlay.overrideSorting = true;
        if (overlay.sortingLayerID != host.sortingLayerID) overlay.sortingLayerID = host.sortingLayerID;
        int want = host.sortingOrder - hostTier + popupTier;
        if (overlay.sortingOrder != want) overlay.sortingOrder = want;
        return overlay;
    }

    private void MeasureRim(Transform board)
    {
        _board = board; _boardChildren = board.childCount;
        _surfaces = board.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
        _rim = 0f;
        foreach (MeshRenderer renderer in _surfaces)
        {
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.renderQueue > 2500) continue;
            MeshFilter? filter = renderer.GetComponent<MeshFilter>();
            Mesh? mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;
            Bounds bounds = mesh.bounds;
            Vector3 center = bounds.center, extent = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3((i & 1) == 0 ? -extent.x : extent.x,
                    (i & 2) == 0 ? -extent.y : extent.y, (i & 4) == 0 ? -extent.z : extent.z);
                _rim = Mathf.Min(_rim, board.InverseTransformPoint(renderer.transform.TransformPoint(corner)).z);
            }
        }
    }

    internal void Restore(RectTransform? popup)
    {
        if (popup == null || !_original.TryGetValue(popup, out Vector3 pose)) return;
        popup.localPosition = pose;
        _original.Remove(popup);
    }

    internal void RestoreAll()
    {
        foreach (var pair in _original)
            if (pair.Key != null) pair.Key.localPosition = pair.Value;
        _original.Clear(); _board = null; _surfaces = System.Array.Empty<MeshRenderer>();
    }
}

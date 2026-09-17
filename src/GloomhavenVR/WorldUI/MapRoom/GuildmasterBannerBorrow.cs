using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Owns one temporary parent change of the original guildmaster banner. Native mode changes
/// use SetParent with worldPositionStays=true: after a VR borrow that preserves the converted
/// window's world pose as an incorrect local pose in the next native parent. Parent ownership
/// may already have returned to the game, but the borrowed root geometry still needs restoring.
/// Children belong to ShowMode (including temple header height) and are never restored here.
/// </summary>
internal sealed class GuildmasterBannerBorrow
{
    private Transform? _banner;
    private Transform? _host;
    private Transform? _home;
    private int _index;
    private Vector3 _position, _scale;
    private Quaternion _rotation;
    private Vector2 _anchorMin, _anchorMax, _pivot, _size;
    private Vector3 _anchoredPosition;

    internal Transform? Home => _home;

    internal bool Owns(Transform banner) => ReferenceEquals(_banner, banner);

    internal bool IsUnder(Transform host) => _banner != null
        && ReferenceEquals(_host, host) && _banner.parent == host;

    internal void Borrow(Transform banner, Transform host)
    {
        if (Owns(banner) && IsUnder(host)) return;
        Release();
        _banner = banner;
        _host = host;
        _home = banner.parent;
        _index = banner.GetSiblingIndex();
        _position = banner.localPosition;
        _rotation = banner.localRotation;
        _scale = banner.localScale;
        if (banner is RectTransform rect)
        {
            _anchorMin = rect.anchorMin;
            _anchorMax = rect.anchorMax;
            _pivot = rect.pivot;
            _size = rect.sizeDelta;
            _anchoredPosition = rect.anchoredPosition3D;
        }
        banner.SetParent(host, worldPositionStays: false);
        banner.SetSiblingIndex(1);
    }

    internal void Release()
    {
        Transform? banner = _banner;
        Transform? host = _host;
        Transform? home = _home;
        // Clear ownership before any Unity setter. Repeated release must not overwrite later
        // native changes, and a replacement HUD must never receive another banner's snapshot.
        _banner = null;
        _host = null;
        _home = null;
        if (banner == null) return;

        if (host != null && banner.parent == host)
        {
            banner.SetParent(home != null ? home : null, worldPositionStays: false);
            if (home != null)
                banner.SetSiblingIndex(Mathf.Clamp(_index, 0, Mathf.Max(0, home.childCount - 1)));
        }
        // If the game moved it first, retain that exact parent and sibling choice. Restore only
        // the saved root-local geometry, removing worldPositionStays' VR scale/position carryover.
        banner.localRotation = _rotation;
        banner.localScale = _scale;
        if (banner is RectTransform rect)
        {
            rect.anchorMin = _anchorMin;
            rect.anchorMax = _anchorMax;
            rect.pivot = _pivot;
            rect.sizeDelta = _size;
            rect.anchoredPosition3D = _anchoredPosition;
        }
        else banner.localPosition = _position;
    }
}

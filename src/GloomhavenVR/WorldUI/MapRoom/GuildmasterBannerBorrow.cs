using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Owns one temporary parent change of the original guildmaster banner. Native mode changes
/// use SetParent with worldPositionStays=true: after a VR borrow that preserves the converted
/// window's world pose as an incorrect local pose in the next native parent. A canonical native
/// pose captured before conversion is expressed in each parent's original coordinate frame,
/// including after that parent is restored. Layout and children remain owned by ShowMode.
/// </summary>
internal sealed class GuildmasterBannerBorrow
{
    private readonly Func<Transform?, Matrix4x4> _nativeFrame;
    private Transform? _banner, _host, _home;
    private Transform? _canonicalBanner, _canonicalParent;
    private Matrix4x4 _canonicalLocal, _canonicalWorld;
    private int _index;

    internal GuildmasterBannerBorrow(Func<Transform?, Matrix4x4>? nativeFrame = null)
    {
        _nativeFrame = nativeFrame ?? (parent => parent != null ? parent.localToWorldMatrix : Matrix4x4.identity);
    }

    internal Transform? Home => _home;
    internal bool Owns(Transform banner) => ReferenceEquals(_banner, banner);
    internal bool IsUnder(Transform host) => _banner != null
        && ReferenceEquals(_host, host) && _banner.parent == host;

    /// <summary>Called before the first destination root is converted. Retained across releases:
    /// temple entry can reparent into an already converted root before the next Borrow.</summary>
    internal void ObserveNative(Transform banner)
    {
        if (ReferenceEquals(_canonicalBanner, banner)) return;
        Release();
        _canonicalBanner = banner;
        _canonicalParent = banner.parent;
        _canonicalLocal = Matrix4x4.TRS(banner.localPosition, banner.localRotation, banner.localScale);
        _canonicalWorld = _nativeFrame(_canonicalParent) * _canonicalLocal;
    }

    internal void Borrow(Transform banner, Transform host)
    {
        if (Owns(banner) && IsUnder(host)) return;
        Release();
        // Runtime establishes the native observation before conversion. This fallback supports
        // callers borrowing an original that has never entered any converted destination.
        ObserveNative(banner);
        _banner = banner;
        _host = host;
        _home = banner.parent;
        _index = banner.GetSiblingIndex();
        banner.SetParent(host, worldPositionStays: false);
        banner.SetSiblingIndex(1);
        ApplyNativePose(banner);
    }

    private void ApplyNativePose(Transform banner)
    {
        Matrix4x4 world = _canonicalParent != null
            ? _nativeFrame(_canonicalParent) * _canonicalLocal : _canonicalWorld;
        Matrix4x4 local = _nativeFrame(banner.parent).inverse * world;
        banner.localPosition = local.GetColumn(3);
        banner.localRotation = local.rotation;
        banner.localScale = local.lossyScale;
        // Anchors, pivot, size and all children remain native-owned. ShowMode can update the
        // root header's height as well; retaining a layout snapshot would undo that decision.
    }

    internal void Reset()
    {
        Release();
        _canonicalBanner = null;
        _canonicalParent = null;
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
        // The game may have reparented first, and the window root may already be restored.
        // Use its native coordinate frame in both orders, never replay one parent's local pose
        // into another parent's coordinates (the repeated-temple regression in build 525).
        ApplyNativePose(banner);
    }
}

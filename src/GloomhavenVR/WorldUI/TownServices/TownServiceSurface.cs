using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Moves an original logical widget section, with its original input and controllers.
/// The conversion owns rollback, including persistent scene membership and native canvas state.</summary>
internal sealed class TownServiceSurface : IDisposable
{
    internal readonly ushort Id;
    internal readonly ConvertedPanel Panel;
    private readonly Vector3 _offset;
    private readonly float _width;
    private readonly GrabbableModal _grab = new();
    private bool _placed;

    internal TownServiceSurface(ushort id, RectTransform source, Vector3 offset, float width)
    {
        Id = id;
        _offset = offset;
        _width = width;
        Panel = CanvasConversion.Convert(source, "TownService." + id, fitContent: false,
            useModLayer: true, transparentBackground: false)
            ?? throw new InvalidOperationException("Native town section could not be converted: " + id);
        try { _grab.Build(Panel, 1f, "TownService." + id); }
        catch { CanvasConversion.Release(Panel); throw; }
    }

    internal void Tick(Vector3 origin, Quaternion yaw, float scale)
    {
        if (!Panel.IsAlive) return;
        if (!_placed)
        {
            float pixels = Mathf.Max(1f, Panel.HostRect.rect.width);
            float factor = _width / pixels / (WorldUIConfig.CanvasScaleMm.Value * .001f);
            _grab.SetExtraScale(factor);
            _grab.SnapFrameTo(origin + yaw * (_offset * scale), yaw);
            _placed = true;
        }
        _grab.Tick();
    }

    internal void LateTick() => _grab.LateSyncHost();

    public void Dispose()
    {
        // Never destroy a root that still contains game-owned UI.
        CanvasConversion.Release(Panel);
        _grab.Destroy();
    }
}

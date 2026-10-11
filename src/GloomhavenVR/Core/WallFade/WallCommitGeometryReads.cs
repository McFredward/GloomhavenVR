using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Exact native MeshRenderer bounds shared only during one synchronous wall
/// publication. Wall commit phases do not write transforms, meshes or localBounds and no
/// native Update/animation runs between them. Particle/skin bounds remain live because their
/// simulation/culling may change after effects are restored. Prepare, drift and per-frame fade
/// calls also remain live. A finally closes the window after failure; no scene reference or
/// old geometric answer survives publication. The existing wall-read-cache setting controls
/// this work alongside the original material cache.
/// </summary>
internal static class WallCommitGeometryReads
{
    private static readonly Dictionary<Renderer, Bounds> Memo = new(4096);
    private static bool _active, _windowOpen;
    internal static long NativeReads { get; private set; }
    internal static long ReusedReads { get; private set; }
    internal static int RetainedCount => Memo.Count;

    internal static void Begin(bool enabled)
    {
        Memo.Clear();
        _active = enabled;
        _windowOpen = true;
        NativeReads = ReusedReads = 0;
    }

    internal static Bounds Read(Renderer renderer)
    {
        if (!(renderer is MeshRenderer)) return renderer.bounds;
        if (!_active)
        {
            if (_windowOpen) NativeReads++;
            return renderer.bounds;
        }
        if (Memo.TryGetValue(renderer, out Bounds bounds))
        {
            ReusedReads++;
            return bounds;
        }
        bounds = renderer.bounds;
        NativeReads++;
        Memo[renderer] = bounds;
        return bounds;
    }

    internal static void End()
    {
        _active = false;
        _windowOpen = false;
        Memo.Clear();
    }
}

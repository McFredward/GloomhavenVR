using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Finite registration and exception-safe borrowing for actual visible native graphics.</summary>
internal static class MenuDepthSources
{
    private static readonly List<IMenuDepthSource> Sources = new(32);
    private static readonly List<IMenuDepthSource> Prepared = new(16);
    private static int _cleanupFailures;
    private static bool _capturing;

    internal static void Register(IMenuDepthSource source)
    {
        if (!Sources.Contains(source)) Sources.Add(source);
    }

    internal static void Unregister(IMenuDepthSource source) => Sources.Remove(source);

    internal static bool HasContent(Transform root)
    {
        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            IMenuDepthSource source = Sources[i];
            if (source.Graphic == null) { Sources.RemoveAt(i); continue; }
            if (Applies(source, root)) return true;
        }
        return false;
    }

    private static bool Applies(IMenuDepthSource source, Transform root) => source.Graphic != null
        && source.Graphic.isActiveAndEnabled && source.Graphic.gameObject.activeInHierarchy
        && source.Graphic.transform.IsChildOf(root) && source.IsReady;

    /// <summary>Call before the private capture; a failing source is also restored.</summary>
    internal static void BeforeCapture(Transform root, MenuDepthEye eye)
    {
        if (_capturing) throw new InvalidOperationException("Nested menu-depth preparation is not owned by this capture.");
        _capturing = true;
        Prepared.Clear();
        try
        {
            // Snapshot membership before callbacks: a native texture setter may
            // synchronously hide or repopulate another original window.
            for (int i = 0; i < Sources.Count; i++)
                if (Applies(Sources[i], root)) Prepared.Add(Sources[i]);
            for (int i = 0; i < Prepared.Count; i++) Prepared[i].BeforeCapture(eye);
        }
        catch
        {
            AfterCapture();
            throw;
        }
    }

    /// <summary>Restore every prepared source, including partial preparation, exactly once.</summary>
    internal static void AfterCapture()
    {
        if (!_capturing) return;
        try
        {
            for (int i = Prepared.Count - 1; i >= 0; i--)
            {
                try { Prepared[i].AfterCapture(); }
                catch (Exception error)
                {
                    int count = ++_cleanupFailures;
                    if (count <= 4 || (count & (count - 1)) == 0)
                        VRLog.Warn("WorldUI", "MENU DEPTH SOURCE RESTORE: " + error.GetType().Name
                            + "; count=" + count + "; native flow continues.");
                }
            }
        }
        finally { Prepared.Clear(); _capturing = false; }
    }
}

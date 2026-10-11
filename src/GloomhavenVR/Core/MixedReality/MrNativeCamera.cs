using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Prevent HDR intermediates without alpha from discarding the transparent clear.
/// The rig head has no image-effect stack; native post-processing remains disabled
/// by CompatModule. This owns only the head's HDR permission, not MSAA, resolution,
/// target textures, other cameras or gameplay materials.
/// </summary>
internal static class MrNativeCamera
{
    private static readonly Dictionary<Camera, bool> Originals = new();

    internal static void Apply(Camera head)
    {
        if (!Originals.ContainsKey(head))
            Originals.Add(head, head.allowHDR);
        if (head.allowHDR)
            head.allowHDR = false;
    }

    internal static void RestoreAll()
    {
        foreach (KeyValuePair<Camera, bool> entry in Originals)
        {
            if (entry.Key != null)
                entry.Key.allowHDR = entry.Value;
        }
        Originals.Clear();
    }
}

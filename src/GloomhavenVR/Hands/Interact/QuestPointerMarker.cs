using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Hands.Interact;

/// <summary>Keep Quest's hit dot on the same native consumer as its visible beam.</summary>
internal static class QuestPointerMarker
{
    internal static GameObject? Create()
    {
        if (!QuestStandalonePlatform.Enabled) return null;
        // Capture131952 has a correctly terminated beam in Menu and Scenario,
        // while its active, unmasked, admitted sphere paints no visible dot. The
        // same sphere worked in Intro. Its Android XR failure is not established
        // by the ordinary GPU fixtures. Reuse the LineRenderer submission that
        // remains visible instead of changing shaders, global depth or game art.
        // Round caps make a bounded dot at the exact same hit, not another ray.
        var root = new GameObject("GloomhavenVR.QuestPointerMarker");
        var line = root.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.loop = false;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 12;
        line.startColor = Color.white;
        line.endColor = Color.white;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        return root;
    }

    internal static void Place(LineRenderer? marker, Vector3 hit, Camera? head, float diameter)
    {
        if (marker == null) return;
        // A zero-length native line has no triangles. Keep a thousandth-diameter
        // spine, centred on the actual collision and oriented across the view.
        // Width and positions are world-space: zooming or a scaled/rotated hand
        // cannot multiply the requested apparent size a second time. LEqual,
        // queue, tint and hit/visibility ownership remain with RayInteractor.
        Vector3 across = head != null ? head.transform.right : Vector3.right;
        Vector3 halfSpine = across * (diameter * .0005f);
        marker.widthMultiplier = diameter;
        marker.SetPosition(0, hit - halfSpine);
        marker.SetPosition(1, hit + halfSpine);
    }
}

using System;
using System.Globalization;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>Two read-only observations of the actual visible pointer consumers.</summary>
internal static class QuestPointerEvidence
{
    private static int _observedKinds;
    internal static bool NeedsObservation => QuestStandalonePlatform.Enabled && VRLog.WantsDebug && _observedKinds != 3;

    internal static void Observe(LineRenderer? own, Transform? marker, Camera? head,
        bool primary, bool scenario, bool menu, bool hit)
    {
        // B680's screenshots show a thin blue continuation at a cyan hit, while
        // the source owns two straight endpoints and has no reflection path.
        // Earlier hit-change samples exhaust their lane in Intro. Observe once
        // in each actual consumer context instead; no normal-level or desktop
        // census, no camera render/readback and no renderer/input writes.
        if (!QuestStandalonePlatform.Enabled || !VRLog.WantsDebug || !hit || head == null
            || own == null || !own.enabled || !own.gameObject.activeInHierarchy || (!scenario && !menu))
            return;
        int kind = scenario ? 2 : 1;
        if ((_observedKinds & kind) != 0) return;
        _observedKinds |= kind; // A failure is bounded too, across hands and rig rebuilds.
        try { UnityEngine.Debug.Log(Describe(own, marker, head, primary, scenario)); }
        catch (Exception error)
        {
            UnityEngine.Debug.Log("[Quest startup] pointer context=" + (scenario ? "scenario" : "menu")
                + " unavailable=" + error.GetType().Name);
        }
    }

    private static string Describe(LineRenderer own, Transform? marker, Camera head, bool primary, bool scenario)
    {
        var text = new StringBuilder(480);
        text.Append("[Quest startup] pointer context=").Append(scenario ? "scenario" : "menu")
            .Append(" primary=").Append(primary).Append(" points=").Append(own.positionCount)
            .Append(" world=").Append(own.useWorldSpace).Append(" loop=").Append(own.loop);
        if (own.positionCount >= 2)
        {
            Vector3 first = WorldPoint(own, 0), last = WorldPoint(own, own.positionCount - 1);
            text.Append(" span=").Append(Vector3.Distance(first, last).ToString("F3", CultureInfo.InvariantCulture));
            text.Append(" screen=").Append(Point(head.WorldToViewportPoint(first))).Append('>')
                .Append(Point(head.WorldToViewportPoint(last)));
            // B681's next hardware report loses the hit marker even though the
            // actual renderer remains active. The pointer's only new native
            // rendering operation was this diagnostic's BakeMesh. Its Android
            // side effects are unproven; do not ask a diagnostic to generate or
            // destroy draw geometry. Passive endpoint/bounds and real consumer
            // state below retain the useful distinction without touching the
            // original LineRenderer's native mesh lifecycle.
            text.Append(" baked=not-requested bounds=")
                .Append(own.bounds.size.magnitude.ToString("F3", CultureInfo.InvariantCulture));
        }
        if (marker != null)
        {
            Renderer? renderer = marker.GetComponent<Renderer>();
            text.Append(" marker=").Append(renderer != null && renderer.enabled && marker.gameObject.activeInHierarchy);
            if (renderer != null)
            {
                text.Append('/').Append(renderer.bounds.size.magnitude.ToString("F3", CultureInfo.InvariantCulture));
                text.Append(" drawnLayer=").Append((head.cullingMask & (1 << marker.gameObject.layer)) != 0);
                Material? material = renderer.sharedMaterial;
                Texture? texture = material != null ? material.mainTexture : null;
                text.Append(" material=").Append(material != null ? material.GetInstanceID() : 0)
                    .Append(" texture=").Append(texture != null ? texture.GetInstanceID() : 0);
                if (texture != null) text.Append('/').Append(texture.width).Append('x').Append(texture.height);
                text.Append(" tintA=").Append(material != null && material.HasProperty("_Color")
                    ? material.color.a.ToString("F2", CultureInfo.InvariantCulture) : "none");
            }
        }
        LineRenderer[] lines = Resources.FindObjectsOfTypeAll<LineRenderer>();
        int active = 0, described = 0, scanned = Math.Min(lines.Length, 64);
        var peers = new StringBuilder(120);
        for (int index = 0; index < scanned; index++)
        {
            LineRenderer line = lines[index];
            if (line == null || !line.enabled || !line.gameObject.activeInHierarchy
                || !line.gameObject.scene.IsValid() || !line.gameObject.scene.isLoaded) continue;
            active++;
            if (ReferenceEquals(line, own) || described >= 2) continue;
            described++;
            peers.Append(' ').Append(Name(line.name)).Append(':').Append(line.positionCount).Append(':')
                .Append((head.cullingMask & (1 << line.gameObject.layer)) != 0);
            if (line.positionCount >= 2)
                peers.Append(':').Append(Point(head.WorldToViewportPoint(WorldPoint(line, 0)))).Append('>')
                    .Append(Point(head.WorldToViewportPoint(WorldPoint(line, line.positionCount - 1))));
        }
        text.Append(" activeLines=").Append(active).Append(" scan=").Append(scanned).Append('/').Append(lines.Length)
            .Append(" peers=").Append(peers.Length == 0 ? "none" : peers.ToString());
        // Names are ASCII-normalized; even adversarial native names retain a
        // single <= 500-byte log record with the geometry facts first.
        if (text.Length > 490) text.Length = 490;
        return text.ToString();
    }

    private static Vector3 WorldPoint(LineRenderer line, int index) => line.useWorldSpace
        ? line.GetPosition(index) : line.transform.TransformPoint(line.GetPosition(index));

    private static string Point(Vector3 value) => string.Format(CultureInfo.InvariantCulture,
        "{0:F2},{1:F2},{2:F2}", value.x, value.y, value.z);

    private static string Name(string value)
    {
        var text = new StringBuilder(12);
        for (int index = 0; index < Math.Min(value.Length, 12); index++)
        {
            char c = value[index];
            text.Append(c >= 33 && c <= 126 ? c : '_');
        }
        return text.ToString();
    }
}

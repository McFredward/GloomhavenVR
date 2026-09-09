using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

/// <summary>Production call-order guard: native widgets may act on pointer-down, so cancelling
/// after dispatch cannot repair a double activation. Unity collider hits need hardware coverage.</summary>
internal static class LaserBarOwnershipVectors
{
    internal static void Run(Harness t, string root)
    {
        string Read(string name) => Regex.Replace(File.ReadAllText(Path.Combine(root,
            "src/GloomhavenVR/Hands/Interact", name)), @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        string ui = Read("RayUguiDriver.cs");
        string grab = Read("RayGrabDriver.cs");
        t.Case("laser-bar/resolve-before-native-events");
        t.True(BeforeEvents(ui), "reachable bar arbitration precedes hover, scroll and pointer-down");
        t.True(!BeforeEvents(ui.Replace("_hand.RayGrab.TryPickBar", "MissingPrepass")),
            "negative control: removing actual prepass fails");
        int at = ui.IndexOf("_hand.RayGrab.TryPickBar", StringComparison.Ordinal);
        t.True(!BeforeEvents(ui.Insert(at, "_pointer.Press(screenPos);")),
            "negative control: activate-on-down before arbitration fails");
        t.True(ui.IndexOf("TrySettingsFallThrough(best", StringComparison.Ordinal) < at,
            "settings redirection cannot bypass the bar after arbitration");
        t.True(Regex.IsMatch(ui, @"barDistance\s*<=\s*bestDist\)[\s\S]*?best\s*=\s*null;"),
            "only a bar at or in front of the chosen canvas removes the UI target");
        t.True(Regex.IsMatch(grab, @"TryPickBar\(out[\s\S]*?\|\|\s*_hand\.RayUgui\.IsPressing"),
            "latched UI press prevents a second grab owner even after aim drifts");
        t.True(grab.Contains("_hand.RayUgui.HitDistance < bestDist"), "nearer UI retains priority");
        string picker = grab.Substring(grab.IndexOf("internal bool TryPickBar", StringComparison.Ordinal));
        picker = picker.Substring(0, picker.IndexOf("private float _nextCarryYieldLogAt", StringComparison.Ordinal));
        t.True(picker.Contains("handle.BarCollider != null ? handle.BarCollider : entries[i].Collider"),
            "visible bar uses dedicated collider; palm zone cannot swallow unrelated widgets");
        t.True(picker.Contains("SolidOccluderDistance < bestDist") && picker.Contains("pick.HitDistance < bestDist"),
            "shared query still respects nearer solid cards, board and physics");
        t.True(!Regex.IsMatch(picker, @"\b(?:ForceGrab|BeginLaserCarry|SetPanelUiHit|SendHaptic|OnGrabHighlight)\s*\("),
            "candidate prepass cannot itself claim input or alter hover/carry");
    }

    private static bool BeforeEvents(string source)
    {
        int pick = source.IndexOf("_hand.RayGrab.TryPickBar", StringComparison.Ordinal);
        if (pick < 0) return false;
        foreach (string call in new[] { "_pointer.SetHovered(", "TickStickScroll();", "_pointer.Press(" })
        {
            int at = source.IndexOf(call, StringComparison.Ordinal);
            if (at < pick) return false;
        }
        return true;
    }
}

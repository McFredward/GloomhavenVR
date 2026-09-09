using System;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Hands.Interact;

namespace GloomhavenVR.WireTests;

/// <summary>Exercise the production arbitration without Unity, then pin each independent
/// dispatch path to it. Collider placement and pointer exits still require a headset test.</summary>
internal static class MapLaserOwnershipVectors
{
    internal static void Run(Harness t, string root)
    {
        float inf = float.PositiveInfinity;
        t.Case("map-laser/foreground-depth");
        foreach (float scale in new[] { 0.01f, 1f, 198f })
        {
            float target = 2f * scale;
            float front = scale;
            float limit = LaserPointerPolicy.PickLimit(20f * scale, inf, front, inf, false);
            t.True(!LaserPointerPolicy.TargetBeforeBlocker(target, limit), "bar blocks farther map icon at every world scale");
            t.True(!LaserPointerPolicy.AllowClick(true, false,
                LaserPointerPolicy.TargetBeforeBlocker(target, limit)), "blocked hover cannot become a click");
            t.True(LaserPointerPolicy.TargetBeforeBlocker(0.5f * scale, limit), "icon in front of bar remains usable");
            t.True(!LaserPointerPolicy.TargetBeforeBlocker(front, limit), "tie belongs to foreground bar");
            t.True(LaserPointerPolicy.TargetBeforeBlocker(target,
                LaserPointerPolicy.PickLimit(20f * scale, inf, inf, inf, false)), "no intersected bar leaves icons usable");
            t.True(!LaserPointerPolicy.TargetBeforeBlocker(target,
                LaserPointerPolicy.PickLimit(20f * scale, front, inf, inf, false)), "solid fan still occludes map");
            t.True(!LaserPointerPolicy.TargetBeforeBlocker(target,
                LaserPointerPolicy.PickLimit(20f * scale, inf, inf, front, false)), "nearer native panel still owns target");
            // Behavioral negative control: the old map-only limit admits precisely this target.
            float oldLimit = Math.Min(20f * scale, inf);
            t.True(LaserPointerPolicy.TargetBeforeBlocker(target, oldLimit), "negative control reproduces old click-through without bar term");
        }
        t.Case("map-laser/carry-release-lifetime");
        t.True(!LaserPointerPolicy.OwnsCarryFrame(false, -1, 0), "new hand has no phantom carry in frame zero");
        bool held = LaserPointerPolicy.OwnsCarryFrame(true, 40, 41);
        bool released = LaserPointerPolicy.OwnsCarryFrame(false, 41, 41);
        bool next = LaserPointerPolicy.OwnsCarryFrame(false, 41, 42);
        foreach (bool owner in new[] { held, released })
        {
            t.True(owner, "held and release frame remain owned even after Held clears");
            t.True(!LaserPointerPolicy.AllowFallback(false, owner), "carry cannot hand hover to idle offhand");
            t.True(!LaserPointerPolicy.AllowClick(true, owner, true), "carry trigger cannot dispatch a map click");
            t.Equal(0f, LaserPointerPolicy.PickLimit(20f, inf, inf, inf, owner), "carry owns ray even after leaving bar geometry");
        }
        t.True(!next && LaserPointerPolicy.AllowFallback(false, next), "following frame restores otherwise valid offhand aim");
        t.True(!LaserPointerPolicy.AllowFallback(true, false), "resting offhand cannot steal active primary beam");
        t.True(LaserPointerPolicy.AllowClick(true, next, true), "unoccluded next press works");
        t.True(!LaserPointerPolicy.AllowClick(false, false, true), "hover alone cannot activate map");
        t.True(!LaserPointerPolicy.AllowClick(true, false, false), "other hand's hover is not a target for clicking hand");
        t.True(LaserPointerPolicy.AllowClick(true, false, true), "negative control: using other hand's target reproduces cross-hand dispatch");

        string Read(string path) => Regex.Replace(File.ReadAllText(Path.Combine(root,
            "src/GloomhavenVR", path)), @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        string map = Read("WorldUI/MapRoom/MapLocationInteractor.cs");
        t.Case("map-laser/production-paths");
        t.True(MapWired(map), "hover and clicking hand use same foreground geometry before dispatch");
        t.True(!MapWired(map.Replace("PickFrom(clicking, out _)", "_hover")), "negative control removes hand-specific re-pick");
        t.True(!MapWired(map.Replace("bar, panel, hand.RayGrab.OwnsPointerFrame", "float.PositiveInfinity, panel, false")),
            "negative control removes actual bar and carry terms from map raycast");
        t.True(map.Contains("if (!LaserPointerPolicy.TargetBeforeBlocker(t, limit))"), "deselect also rejects a foreground bar");
        foreach (string path in new[] { "WorldUI/MapRoom/MapButtonRail.cs", "WorldUI/Surfaces/CombatLogSurface.cs" })
        {
            string source = Read(path);
            t.True(source.Contains("hand.RayGrab.OwnsPointerFrame")
                && source.Contains("RayGrabDriver.OccludingBarDistance(origin, direction,")
                && source.Contains("!LaserPointerPolicy.TargetBeforeBlocker("), path + " blocks both hover and click behind bars");
        }
        string flat = Read("WorldUI/FlatScreen/FlatScreen.6.Pointer.cs");
        int block = flat.IndexOf("!LaserPointerPolicy.TargetBeforeBlocker(dist, bar)", StringComparison.Ordinal);
        t.True(block > 0 && block < flat.IndexOf("VirtualMouse.WarpTo", StringComparison.Ordinal),
            "flat menu resolves bar before virtual mouse hover");
        t.True(FlatYieldWired(flat), "confirmed blockers retire the stale screen pixel before an inactive-pick return");
        t.True(!FlatYieldWired(flat.Replace("VirtualMouse.WarpTo(VirtualMouse.ParkPixel);", "")),
            "negative control: HideReticle without clearing the mouse leaves stale hover");
        t.True(!FlatYieldWired(flat.Replace("YieldScreenPointer();", "HideReticle();")),
            "negative control: disconnected immediate-yield path is rejected");
        int carry = flat.IndexOf("hand.RayGrab.OwnsPointerFrame", StringComparison.Ordinal);
        t.True(flat.IndexOf("if (_pokePressing)", StringComparison.Ordinal) < carry,
            "active fingertip owns the virtual mouse before laser carry arbitration");
        string yield = flat.Substring(flat.IndexOf("private void YieldScreenPointer()", StringComparison.Ordinal));
        yield = yield.Substring(0, yield.IndexOf("private void HideReticle()", StringComparison.Ordinal));
        t.True(yield.Contains("if (_pokePressing) return;") && !yield.Contains("DirectClick("),
            "yield protects a fingertip owner and never clicks on abort");
        string hide = flat.Substring(flat.IndexOf("private void HideReticle()", StringComparison.Ordinal));
        hide = hide.Substring(0, hide.IndexOf("private static readonly", StringComparison.Ordinal));
        t.True(hide.Contains("_latched = false;") && hide.Contains("_stereo.EndMapPan();")
            && hide.Contains("EndScreenDrag();") && !hide.Contains("DirectClick("),
            "screen cancellation clears latch, pan and native drag without a click");
        string grab = Read("Hands/Interact/RayGrabDriver.cs");
        t.True(grab.Contains("else _lastCarryFrame = Time.frameCount;"), "successful same-frame ForceGrab claims release lifetime");
        t.True(grab.Contains("if (_hand.Grabber.Held is PanelGrabHandle) _lastCarryFrame = Time.frameCount;"),
            "carry observed before ProximityGrabber releases preserves that frame");
    }

    private static bool FlatYieldWired(string source)
    {
        int carry = source.IndexOf("hand.RayGrab.OwnsPointerFrame", StringComparison.Ordinal);
        int pick = source.IndexOf("!pick.TryGetPick(out PickPose pose)", StringComparison.Ordinal);
        return carry >= 0 && carry < pick
            && source.Contains("VirtualMouse.WarpTo(VirtualMouse.ParkPixel);")
            && Regex.IsMatch(source, @"hand\.RayGrab\.OwnsPointerFrame\)[\s\S]*?\{\s*YieldScreenPointer\(\);")
            && Regex.IsMatch(source, @"!LaserPointerPolicy\.TargetBeforeBlocker\(dist, bar\)\)[\s\S]*?\{\s*YieldScreenPointer\(\);")
            && Regex.IsMatch(source, @"hand\.RayUgui\.HitDistance < dist - 0\.005f\)[\s\S]*?\{\s*YieldScreenPointer\(\);");
    }

    private static bool MapWired(string source) =>
        source.Contains("MapLocation? clicked = PickFrom(clicking, out _);")
        && source.Contains("Dispatch(clicked!,")
        && source.Contains("bar, panel, hand.RayGrab.OwnsPointerFrame")
        && source.Contains("LaserPointerPolicy.AllowFallback(primaryHasBeam,")
        && source.IndexOf("LaserPointerPolicy.PickLimit(reach", StringComparison.Ordinal)
            < source.IndexOf("Physics.RaycastNonAlloc(pick.Origin", StringComparison.Ordinal);
}

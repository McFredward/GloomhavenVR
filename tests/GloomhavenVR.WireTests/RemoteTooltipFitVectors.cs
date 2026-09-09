using System;
using System.IO;

namespace GloomhavenVR.WireTests;

/// <summary>Production source bindings for native tooltip ownership. Unity geometry and final
/// headset rendering remain hardware checks; these guards catch the actual regression paths.</summary>
internal static class RemoteTooltipFitVectors
{
    internal static void Run(Harness t, string root)
    {
        string Read(string name) => File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/" + name + ".cs"));
        string mirror = Read("RemoteWidgetMirror"), widgets = Read("RemoteUseBarWidgets"), tooltip = Read("RemoteUseBarTooltip");
        t.Case("native tooltip: only the original hover branches leave the panel's fitted extent");
        t.True(RemoteCapVisibilityVectors.Method(widgets, "internal RemoteUseBarWidgets(")
            .Contains("excludedFromFitBranch: IsOriginalTooltipRoot"), "actual use-bar constructor binds original tooltip roots");
        t.True(RemoteCapVisibilityVectors.Method(widgets, "private bool IsOriginalTooltipRoot(")
            .Contains("slot.Tooltip?.OwnsRoot(source)"), "only captured original tooltip roots are excluded");
        t.True(ExcludesBounds(mirror), "actual native measurement skips the complete excluded subtree");
        t.True(!ExcludesBounds(mirror.Replace(" || pairs[i].ExcludedFromFit", "")),
            "negative control: reverting the measurement predicate restores the hover resize defect");
        string apply = RemoteCapVisibilityVectors.Method(mirror, "public bool Apply(bool isRoot, bool driveRects)");
        t.True(CopiesVisuals(apply), "fit exclusion cannot suppress actual picture/async artwork copying");
        t.True(!CopiesVisuals("if (ExcludedFromFit) return false;" + apply),
            "negative control: treating measured exclusion as visibility ownership fails");

        t.Case("native tooltip: return the original borrowed item before destroying its holder");
        string item = RemoteCapVisibilityVectors.Method(tooltip, "private void PrepareItem(");
        t.True(SafeReturn(item), "actual pool return uses the shared detaching helper before temporary destruction");
        t.True(!SafeReturn(item.Replace("RemoteItemCardSource.ReturnBorrowed", "ObjectPool.RecycleCard")),
            "negative control: direct native item recycle leaves the pool object under the destroyed holder");
    }

    private static bool ExcludesBounds(string source)
    {
        string body = RemoteCapVisibilityVectors.Method(source, "private bool TryMeasure(out Bounds bounds)");
        return body.Contains("pairs[i].External || pairs[i].ExcludedFromFit")
            && body.Contains("int past = pairs[i].SkipTo") && body.Contains("past - 1");
    }

    private static bool CopiesVisuals(string apply) => apply.Length > 0 && !apply.Contains("ExcludedFromFit");
    private static bool SafeReturn(string item)
    {
        int giveBack = item.IndexOf("RemoteItemCardSource.ReturnBorrowed(item.ID, borrowed)", StringComparison.Ordinal);
        int destroy = item.IndexOf("UnityEngine.Object.Destroy(holder)", StringComparison.Ordinal);
        return giveBack >= 0 && destroy > giveBack && item.Contains("finally");
    }
}

using System.Runtime.CompilerServices;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class PreparedReturn { }
    private static readonly ConditionalWeakTable<Transform, PreparedReturn> PreparedReturns = new();

    /// <summary>Freeze the same original in the independent cosmetic lane while it is
    /// still offered. Its complete baseline is cacheable, but never a second visible card.</summary>
    internal static void PrepareCardReturn(Transform source, CardReturnSampler sample, VRHand? hand = null)
    {
        if (source == null) return;
        RegisterCardReturn(source, sample, hand);
        PreparedReturns.GetValue(source, _ => new PreparedReturn());
    }
    internal static void ExposeCommittedCardOriginal(Transform source)
    { if (source != null) PreparedReturns.Remove(source); }

    internal static bool HidePreparedCardReturn(Transform source)
    {
        if (!ReferenceEquals(_local, StockLane) || _sharedFrame == null) return false;
        for (Transform? node = source; node != null; node = node.parent)
            if (PreparedReturns.TryGetValue(node, out _))
            {
                CardReturnReference? returning = CardReturn(source);
                return returning == null || !returning.Sample(source, _sharedFrame, returning.Hand, out _, out _);
            }
        return false;
    }

    internal static bool PrepareHiddenCardReturnOriginal(TownServiceFrame frame) => frame.VisitorStock
        && frame.Service == 1 && !frame.PublicCatalog
        && (TryStockItemId(frame.TemplateAddress, out _) || CosmeticAbilityFace(frame.TemplateAddress)
            || frame.TemplateAddress.StartsWith("inspectionbody.", System.StringComparison.Ordinal)
            || frame.TemplateAddress.StartsWith("map.cardbody|", System.StringComparison.Ordinal));
}

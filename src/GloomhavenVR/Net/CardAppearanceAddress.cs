using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

internal static class CardAppearanceAddress
{
    // A rest offer lacks the VR pile marker; a dialog may also use another widget for its
    // model. Resolve the receiver's canonical pile arc by model, never a stale widget type.
    internal static bool TryPile<TWidget, TCard>(IReadOnlyList<TWidget> arc, TCard model,
        Func<TWidget, TCard?> cardOf, bool burnt, out byte code, out byte count) where TCard : class
    {
        code = count = 0;
        if (model == null || arc.Count == 0 || arc.Count > byte.MaxValue) return false;
        int seat = -1;
        for (int i = 0; i < arc.Count; i++)
        {
            if (!ReferenceEquals(cardOf(arc[i]), model)) continue;
            if (seat >= 0) return false;
            seat = i;
        }
        if (seat < 0 || seat >= NetProtocol.HeldFaceIndexUnknown) return false;
        code = NetProtocol.EncodeHeldFace(burnt ? NetProtocol.HeldFaceListBurnt : NetProtocol.HeldFaceListDiscard, seat);
        count = (byte)arc.Count;
        return true;
    }
}

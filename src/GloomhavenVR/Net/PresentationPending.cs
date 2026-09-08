using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Retains an opening pose and the latest identity boundary while coalescing a receive
/// burst. The samples on both sides of a clear/reopen must survive together.</summary>
internal static class PresentationPending
{
    internal static void Append<T>(List<T> samples, T value, Func<T, T, bool> sameIdentity)
    {
        samples.Add(value);
        if (samples.Count <= 4) return;
        int boundary = -1;
        for (int i = 1; i < samples.Count; i++)
            if (!sameIdentity(samples[i - 1], samples[i])) boundary = i;
        for (int i = 1; i < samples.Count - 1; i++)
        {
            if (i == boundary || i == boundary - 1) continue;
            samples.RemoveAt(i); return;
        }
        throw new InvalidOperationException("Presentation queue exceeded its boundary budget.");
    }

    internal static bool SameNativeIdentity(NativeUseBarSnapshot a, NativeUseBarSnapshot b)
    {
        NativeUseBarState? x = a.State, y = b.State;
        if (x == null || y == null) return x == null && y == null;
        return x.Bar == y.Bar && x.Slot == y.Slot && x.ActorId == y.ActorId
            && x.ModelKind == y.ModelKind && x.ModelId == y.ModelId && x.ModelIndex == y.ModelIndex
            && x.ModelCount == y.ModelCount && x.SlotIdentity == y.SlotIdentity;
    }

    internal static bool SamePlumeIdentity(CardPlumeSnapshot a, CardPlumeSnapshot b)
    {
        if (a.States.Length != b.States.Length) return false;
        foreach (CardPlumeState x in a.States)
        {
            bool found = false;
            foreach (CardPlumeState y in b.States)
                if (x.ActorId == y.ActorId && x.FaceCode == y.FaceCode && x.EmitterIndex == y.EmitterIndex && x.Episode == y.Episode)
                { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    internal static bool SameBonusIdentity(UseBarAnimationSnapshot a, UseBarAnimationSnapshot b)
    {
        if (a.States.Length != b.States.Length) return false;
        foreach (UseBarAnimationState x in a.States)
        {
            bool found = false;
            foreach (UseBarAnimationState y in b.States)
                if (x.Slot == y.Slot && x.ActorId == y.ActorId && x.SlotIdentity == y.SlotIdentity)
                { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
}
